using System.Collections.Generic;
using TinyEvents.SourceGen.Model;
using Microsoft.CodeAnalysis;

namespace TinyEvents.SourceGen.Validation;

internal static class TinyEventsDiagnostics
{
    private static readonly DiagnosticDescriptor OpenGenericConsumer = new DiagnosticDescriptor(
        "TEV001",
        "Open generic event consumers are not supported",
        "{0}",
        "TinyEvents.SourceGeneration",
        DiagnosticSeverity.Warning,
        true);

    public static void Report(
        SourceProductionContext context,
        IReadOnlyList<GenerationIssue> issues)
    {
        foreach (var issue in issues)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DescriptorFor(issue),
                null,
                issue.Message));
        }
    }

    private static DiagnosticDescriptor DescriptorFor(GenerationIssue issue)
    {
        if (issue.Id == OpenGenericConsumer.Id)
        {
            return OpenGenericConsumer;
        }

        return new DiagnosticDescriptor(
            issue.Id,
            "TinyEvents source generation issue",
            "{0}",
            "TinyEvents.SourceGeneration",
            DiagnosticSeverity.Warning,
            true);
    }
}
