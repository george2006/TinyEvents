using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;

namespace TinyEvents.SourceGen.Tests;

public static class SourceGeneratorTestHost
{
    public static GeneratorDriverRunResult Run(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new TinyEventsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out _);

        return driver.GetRunResult();
    }

    public static Assembly CompileAndLoad(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new TinyEventsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var generatedCompilation,
            out var diagnostics);

        ThrowIfDiagnosticsFailed(diagnostics);
        return LoadAssembly(generatedCompilation);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            "TinyEvents.SourceGen.TestAssembly." + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)) },
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference[] References()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(RequiredReferences())
            .DistinctBy(reference => reference.Display)
            .ToArray();
    }

    private static IEnumerable<MetadataReference> RequiredReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(ITinyEventPublisher).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollection).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions).Assembly.Location);
    }

    private static Assembly LoadAssembly(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        ThrowIfDiagnosticsFailed(result.Diagnostics);

        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }

    private static void ThrowIfDiagnosticsFailed(IEnumerable<Diagnostic> diagnostics)
    {
        var failures = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (failures.Length == 0)
        {
            return;
        }

        var message = string.Join(Environment.NewLine, failures.Select(diagnostic => diagnostic.ToString()));
        throw new InvalidOperationException(message);
    }
}
