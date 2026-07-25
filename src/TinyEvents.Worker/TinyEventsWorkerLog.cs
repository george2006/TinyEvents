using Microsoft.Extensions.Logging;

namespace TinyEvents.Worker;

internal static partial class TinyEventsWorkerLog
{
    public static void IterationFailed(
        ILogger logger,
        TinyEventsWorkerFailure failure,
        Exception exception)
    {
        switch (failure.ReportKind)
        {
            case TinyEventsWorkerFailureReportKind.Warning:
                IterationFailed(logger, failure.Count, exception);
                break;
            case TinyEventsWorkerFailureReportKind.Prominent:
                RepeatedFailures(logger, failure.Count, exception);
                break;
        }
    }

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
        EventId = 1104,
        EventName = "RepeatedWorkerFailures",
        Level = LogLevel.Error,
        Message = "TinyEvents worker has failed {ConsecutiveFailures} consecutive processing iterations and will continue retrying.")]
    private static partial void RepeatedFailures(
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
