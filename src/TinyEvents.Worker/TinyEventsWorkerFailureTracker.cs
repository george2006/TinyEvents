namespace TinyEvents.Worker;

internal sealed class TinyEventsWorkerFailureTracker
{
    private int consecutiveFailureCount;

    public TinyEventsWorkerFailure RecordFailure()
    {
        consecutiveFailureCount++;

        var reportKind = GetReportKind(consecutiveFailureCount);
        return new TinyEventsWorkerFailure(consecutiveFailureCount, reportKind);
    }

    public bool TryReset(out int previousFailureCount)
    {
        previousFailureCount = consecutiveFailureCount;

        if (consecutiveFailureCount == 0)
        {
            return false;
        }

        consecutiveFailureCount = 0;
        return true;
    }

    private static TinyEventsWorkerFailureReportKind GetReportKind(int failureCount)
    {
        if (failureCount <= 4)
        {
            return TinyEventsWorkerFailureReportKind.Warning;
        }

        if (failureCount is 5 or 10 or 20 or 50 || failureCount % 100 == 0)
        {
            return TinyEventsWorkerFailureReportKind.Prominent;
        }

        return TinyEventsWorkerFailureReportKind.None;
    }
}

internal readonly record struct TinyEventsWorkerFailure(
    int Count,
    TinyEventsWorkerFailureReportKind ReportKind);

internal enum TinyEventsWorkerFailureReportKind
{
    None,
    Warning,
    Prominent
}
