using Microsoft.Extensions.DependencyInjection;
using TinyEvents.Worker;
using Xunit;

namespace TinyEvents.Worker.Tests;

public sealed class TinyEventsCleanupTests
{
    [Fact]
    public async Task Cleanup_uses_one_clock_read_and_configured_retention_and_batch_size()
    {
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var store = new RecordingCleanupStore();
        var options = new TinyEventsWorkerOptions
        {
            ProcessedRetention = TimeSpan.FromHours(2),
            CleanupBatchSize = 37
        };
        var cleanup = new TinyOutboxCleanup(
            store,
            options,
            new FixedTimeProvider(now));

        var result = await cleanup.DeleteExpiredProcessedAsync(
            CancellationToken.None);

        Assert.Equal(now.AddHours(-2), store.CutoffUtc);
        Assert.Equal(37, store.MaxCount);
        Assert.Equal(now.AddHours(-2), result.CutoffUtc);
        Assert.Equal(3, result.DeletedCount);
    }

    [Fact]
    public async Task Cleanup_background_service_resolves_cleanup_from_a_new_scope()
    {
        ScopedCleanupStore.InstanceIds.Clear();
        var services = new ServiceCollection();
        services.AddSingleton(new TinyEventsWorkerOptions());
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<ITinyOutboxCleanupStore, ScopedCleanupStore>();
        services.AddScoped<TinyOutboxCleanup>();
        services.AddSingleton<TinyEventsCleanupBackgroundService>();
        using var provider = services.BuildServiceProvider();
        var backgroundService =
            provider.GetRequiredService<TinyEventsCleanupBackgroundService>();

        await backgroundService.DeleteOnceAsync();
        await backgroundService.DeleteOnceAsync();

        Assert.Equal(2, ScopedCleanupStore.InstanceIds.Count);
        Assert.NotEqual(
            ScopedCleanupStore.InstanceIds[0],
            ScopedCleanupStore.InstanceIds[1]);
    }

    [Fact]
    public async Task Cleanup_background_service_fails_startup_when_cleanup_store_is_missing()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TinyEventsWorkerOptions());
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<TinyOutboxCleanup>();
        services.AddSingleton<TinyEventsCleanupBackgroundService>();
        using var provider = services.BuildServiceProvider();
        var backgroundService =
            provider.GetRequiredService<TinyEventsCleanupBackgroundService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backgroundService.StartAsync(CancellationToken.None));

        Assert.Contains(
            nameof(ITinyOutboxCleanupStore),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_cleanup_does_not_require_a_cleanup_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TinyEventsWorkerOptions
        {
            CleanupEnabled = false
        });
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<TinyOutboxCleanup>();
        services.AddSingleton<TinyEventsCleanupBackgroundService>();
        using var provider = services.BuildServiceProvider();
        var backgroundService =
            provider.GetRequiredService<TinyEventsCleanupBackgroundService>();

        await backgroundService.StartAsync(CancellationToken.None);
        await backgroundService.StopAsync(CancellationToken.None);
    }

    private sealed class RecordingCleanupStore : ITinyOutboxCleanupStore
    {
        public DateTimeOffset CutoffUtc { get; private set; }

        public int MaxCount { get; private set; }

        public ValueTask<int> DeleteProcessedBeforeAsync(
            DateTimeOffset cutoffUtc,
            int maxCount,
            CancellationToken cancellationToken)
        {
            CutoffUtc = cutoffUtc;
            MaxCount = maxCount;
            return ValueTask.FromResult(3);
        }
    }

    private sealed class ScopedCleanupStore : ITinyOutboxCleanupStore
    {
        private readonly Guid instanceId = Guid.NewGuid();

        public static List<Guid> InstanceIds { get; } = new List<Guid>();

        public ValueTask<int> DeleteProcessedBeforeAsync(
            DateTimeOffset cutoffUtc,
            int maxCount,
            CancellationToken cancellationToken)
        {
            InstanceIds.Add(instanceId);
            return ValueTask.FromResult(0);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
