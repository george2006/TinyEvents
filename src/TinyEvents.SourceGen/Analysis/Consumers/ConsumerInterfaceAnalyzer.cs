using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace TinyEvents.SourceGen.Analysis.Consumers;

internal static class ConsumerInterfaceAnalyzer
{
    public static IReadOnlyList<INamedTypeSymbol> FindConsumerInterfaces(
        INamedTypeSymbol implementationType,
        INamedTypeSymbol consumerContract)
    {
        var matches = new List<INamedTypeSymbol>();

        foreach (var interfaceType in implementationType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, consumerContract))
            {
                matches.Add(interfaceType);
            }
        }

        return matches;
    }
}
