using System.Collections.Generic;
using System.Collections.Immutable;
using TinyEvents.SourceGen.Model;
using Microsoft.CodeAnalysis;

namespace TinyEvents.SourceGen.Analysis.Consumers;

internal static class ConsumerDiscoveryAnalyzer
{
    private const string ConsumerContractMetadataName = "TinyEvents.IEventConsumer`1";

    public static DiscoveryResult Discover(
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidates)
    {
        var consumerContract = compilation.GetTypeByMetadataName(ConsumerContractMetadataName);

        if (consumerContract is null)
        {
            return DiscoveryResult.Empty;
        }

        return DiscoverConsumers(candidates, consumerContract);
    }

    private static DiscoveryResult DiscoverConsumers(
        ImmutableArray<INamedTypeSymbol> candidates,
        INamedTypeSymbol consumerContract)
    {
        var consumers = new List<DiscoveredConsumer>();
        var issues = new List<GenerationIssue>();

        foreach (var implementationType in candidates)
        {
            AnalyzeConsumer(consumers, issues, implementationType, consumerContract);
        }

        return new DiscoveryResult(consumers, issues);
    }

    private static void AnalyzeConsumer(
        List<DiscoveredConsumer> consumers,
        List<GenerationIssue> issues,
        INamedTypeSymbol implementationType,
        INamedTypeSymbol consumerContract)
    {
        var matchingInterfaces = ConsumerInterfaceAnalyzer.FindConsumerInterfaces(
            implementationType,
            consumerContract);

        if (matchingInterfaces.Count == 0)
        {
            return;
        }

        if (!ConsumerShapeAnalyzer.CanGenerate(implementationType))
        {
            return;
        }

        foreach (var interfaceType in matchingInterfaces)
        {
            AddConsumer(consumers, issues, implementationType, interfaceType.TypeArguments[0]);
        }
    }

    private static void AddConsumer(
        List<DiscoveredConsumer> consumers,
        List<GenerationIssue> issues,
        INamedTypeSymbol implementationType,
        ITypeSymbol eventType)
    {
        if (eventType.TypeKind == TypeKind.TypeParameter)
        {
            issues.Add(GenerationIssue.OpenGenericConsumer(
                implementationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return;
        }

        consumers.Add(ConsumerModelFactory.Create(implementationType, eventType));
    }
}
