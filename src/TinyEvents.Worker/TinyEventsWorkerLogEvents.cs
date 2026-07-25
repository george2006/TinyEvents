using Microsoft.Extensions.Logging;

namespace TinyEvents.Worker;

internal static class TinyEventsWorkerLogEvents
{
    public static readonly EventId WorkerIterationFailed = new(
        1100,
        nameof(WorkerIterationFailed));
}
