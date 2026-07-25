using Microsoft.Extensions.Logging;

namespace TinyEvents.Worker;

internal static partial class TinyEventsWorkerLog
{
    [LoggerMessage(
        EventId = 1100,
        EventName = "WorkerIterationFailed",
        Level = LogLevel.Warning,
        Message = "TinyEvents worker processing iteration failed. Consecutive failures: {ConsecutiveFailures}.")]
    public static partial void IterationFailed(
        ILogger logger,
        int consecutiveFailures,
        Exception exception);

    [LoggerMessage(
        EventId = 1101,
        EventName = "WorkerRecovered",
        Level = LogLevel.Information,
        Message = "TinyEvents worker recovered after {ConsecutiveFailures} consecutive failed iterations.")]
    public static partial void Recovered(
        ILogger logger,
        int consecutiveFailures);
}
