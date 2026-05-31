using Microsoft.CodeAnalysis;

namespace TinyEvents.SourceGen.Analysis.Consumers;

internal static class ConsumerShapeAnalyzer
{
    public static bool CanGenerate(INamedTypeSymbol implementationType)
    {
        if (implementationType.IsAbstract)
        {
            return false;
        }

        if (!IsAccessibleFromGeneratedCode(implementationType))
        {
            return false;
        }

        return ContainingTypesAreAccessible(implementationType);
    }

    private static bool ContainingTypesAreAccessible(INamedTypeSymbol implementationType)
    {
        var containingType = implementationType.ContainingType;

        while (containingType is not null)
        {
            if (!IsAccessibleFromGeneratedCode(containingType))
            {
                return false;
            }

            containingType = containingType.ContainingType;
        }

        return true;
    }

    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol symbol)
    {
        return symbol.DeclaredAccessibility == Accessibility.Public
            || symbol.DeclaredAccessibility == Accessibility.Internal;
    }
}
