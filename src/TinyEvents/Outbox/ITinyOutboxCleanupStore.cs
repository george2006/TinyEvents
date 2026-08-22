namespace TinyEvents;

public interface ITinyOutboxCleanupStore
{
    ValueTask<int> DeleteProcessedBeforeAsync(
        DateTimeOffset cutoffUtc,
        int maxCount,
        CancellationToken cancellationToken);
}
