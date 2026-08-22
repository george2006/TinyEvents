using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TinyEvents.Worker;

internal sealed class TinyEventsCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly TinyEventsCleanupStartupValidator startupValidator;
    private readonly TinyEventsWorkerOptions options;
    private readonly ILogger<TinyEventsCleanupBackgroundService> logger;

    public TinyEventsCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TinyEventsWorkerOptions options)
        : this(
            scopeFactory,
            options,
            NullLogger<TinyEventsCleanupBackgroundService>.Instance)
    {
    }

    public TinyEventsCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TinyEventsWorkerOptions options,
        ILogger<TinyEventsCleanupBackgroundService> logger)
    {
        this.scopeFactory = scopeFactory
            ?? throw new ArgumentNullException(nameof(scopeFactory));
        startupValidator = new TinyEventsCleanupStartupValidator(scopeFactory);
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal async ValueTask<TinyOutboxCleanupResult> DeleteOnceAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<TinyOutboxCleanup>();

        return await cleanup.DeleteExpiredProcessedAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();

        if (!options.CleanupEnabled)
        {
            return;
        }

        startupValidator.ValidateConfiguration();

        var failures = new TinyEventsWorkerFailureTracker();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cleanup = await DeleteOnceAsync(stoppingToken);

                if (cleanup.DeletedCount > 0)
                {
                    TinyEventsCleanupLog.BatchDeleted(
                        logger,
                        cleanup.DeletedCount,
                        cleanup.CutoffUtc);
                }

                if (failures.TryReset(out var previousFailureCount))
                {
                    TinyEventsCleanupLog.Recovered(logger, previousFailureCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var failure = failures.RecordFailure();
                TinyEventsCleanupLog.IterationFailed(logger, failure, exception);
            }

            try
            {
                await Task.Delay(options.CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
