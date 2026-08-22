namespace TinyEvents.Worker;

internal sealed class TinyOutboxCleanup
{
    private readonly ITinyOutboxCleanupStore store;
    private readonly TinyEventsWorkerOptions options;
    private readonly TimeProvider timeProvider;

    public TinyOutboxCleanup(
        ITinyOutboxCleanupStore store,
        TinyEventsWorkerOptions options,
        TimeProvider timeProvider)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<TinyOutboxCleanupResult> DeleteExpiredProcessedAsync(
        CancellationToken cancellationToken)
    {
        var cutoffUtc = timeProvider.GetUtcNow().Subtract(options.ProcessedRetention);
        var deletedCount = await store.DeleteProcessedBeforeAsync(
            cutoffUtc,
            options.CleanupBatchSize,
            cancellationToken);

        return new TinyOutboxCleanupResult(cutoffUtc, deletedCount);
    }
}
