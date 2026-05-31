using System.Collections.Generic;

namespace TinyEvents.SourceGen.Model;

internal sealed class DiscoveryResult
{
    public static readonly DiscoveryResult Empty = new DiscoveryResult(
        new DiscoveredConsumer[0],
        new GenerationIssue[0]);

    public DiscoveryResult(
        IReadOnlyList<DiscoveredConsumer> consumers,
        IReadOnlyList<GenerationIssue> issues)
    {
        Consumers = consumers;
        Issues = issues;
    }

    public IReadOnlyList<DiscoveredConsumer> Consumers { get; }

    public IReadOnlyList<GenerationIssue> Issues { get; }
}
