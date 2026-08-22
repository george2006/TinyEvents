using Microsoft.Extensions.Logging;

namespace TinyEvents.Worker;

internal static partial class TinyEventsCleanupLog
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
        EventId = 1110,
        EventName = "CleanupBatchDeleted",
        Level = LogLevel.Debug,
        Message = "TinyEvents deleted {DeletedCount} processed outbox messages older than {CutoffUtc}.")]
    public static partial void BatchDeleted(
        ILogger logger,
        int deletedCount,
        DateTimeOffset cutoffUtc);

    [LoggerMessage(
        EventId = 1111,
        EventName = "CleanupIterationFailed",
        Level = LogLevel.Warning,
        Message = "TinyEvents cleanup iteration failed. Consecutive failures: {ConsecutiveFailures}.")]
    private static partial void IterationFailed(
        ILogger logger,
        int consecutiveFailures,
        Exception exception);

    [LoggerMessage(
        EventId = 1112,
        EventName = "RepeatedCleanupFailures",
        Level = LogLevel.Error,
        Message = "TinyEvents cleanup has failed {ConsecutiveFailures} consecutive iterations and will continue retrying.")]
    private static partial void RepeatedFailures(
        ILogger logger,
        int consecutiveFailures,
        Exception exception);

    [LoggerMessage(
        EventId = 1113,
        EventName = "CleanupRecovered",
        Level = LogLevel.Information,
        Message = "TinyEvents cleanup recovered after {ConsecutiveFailures} consecutive failed iterations.")]
    public static partial void Recovered(
        ILogger logger,
        int consecutiveFailures);
}
