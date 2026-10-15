namespace TinyEvents;

public sealed class TinyOutboxStatus
{
    public required DateTimeOffset ObservedAtUtc { get; init; }

    public long PendingCount { get; init; }

    public long ProcessingCount { get; init; }

    public long ExpiredProcessingCount { get; init; }

    public long FailedCount { get; init; }

    public DateTimeOffset? OldestPendingAtUtc { get; init; }

    public DateTimeOffset? LastProcessedAtUtc { get; init; }

    public TimeSpan? OldestPendingAge
    {
        get
        {
            if (OldestPendingAtUtc is not { } oldestPendingAtUtc)
            {
                return null;
            }

            var age = ObservedAtUtc - oldestPendingAtUtc;
            return age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }
    }
}
