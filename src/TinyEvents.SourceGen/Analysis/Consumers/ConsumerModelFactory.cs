using TinyEvents.SourceGen.Model;
using Microsoft.CodeAnalysis;

namespace TinyEvents.SourceGen.Analysis.Consumers;

internal static class ConsumerModelFactory
{
    public static DiscoveredConsumer Create(
        INamedTypeSymbol implementationType,
        ITypeSymbol eventType)
    {
        return new DiscoveredConsumer(
            implementationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            eventType.ToDisplayString());
    }
}
