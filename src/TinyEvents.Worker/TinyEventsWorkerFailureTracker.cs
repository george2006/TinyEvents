namespace TinyEvents.Worker;

internal sealed class TinyEventsWorkerFailureTracker
{
    private int consecutiveFailureCount;

    public int RecordFailure()
    {
        consecutiveFailureCount++;
        return consecutiveFailureCount;
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
}
