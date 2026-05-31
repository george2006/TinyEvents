using System.Text;
using System.Threading;
using TinyEvents.SourceGen.Analysis.Consumers;
using TinyEvents.SourceGen.Emission;
using TinyEvents.SourceGen.Planning;
using TinyEvents.SourceGen.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TinyEvents.SourceGen;

[Generator]
public sealed class TinyEventsSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidateConsumers = context.SyntaxProvider
            .CreateSyntaxProvider(IsClassDeclaration, GetCandidateConsumer)
            .Where(static consumer => consumer is not null)
            .Select(static (consumer, _) => consumer!)
            .Collect();

        var discovery = context.CompilationProvider
            .Combine(candidateConsumers)
            .Select(static (source, _) => ConsumerDiscoveryAnalyzer.Discover(
                source.Left,
                source.Right));

        context.RegisterSourceOutput(discovery, Emit);
    }

    private static bool IsClassDeclaration(
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax;
    }

    private static INamedTypeSymbol? GetCandidateConsumer(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        return context.SemanticModel.GetDeclaredSymbol(
            context.Node,
            cancellationToken) as INamedTypeSymbol;
    }

    private static void Emit(
        SourceProductionContext context,
        Model.DiscoveryResult discovery)
    {
        TinyEventsDiagnostics.Report(context, discovery.Issues);
        var plan = TinyEventsGenerationPlanner.Plan(discovery);

        if (!plan.HasContent)
        {
            return;
        }

        context.AddSource(
            "TinyEvents.GeneratedContribution.g.cs",
            SourceText.From(ThisAssemblyContributionEmitter.Emit(plan), Encoding.UTF8));
    }
}
