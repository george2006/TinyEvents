using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TinyEvents.Worker;

public sealed class TinyEventsBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly TinyEventsWorkerOptions options;
    private readonly ILogger<TinyEventsBackgroundService> logger;

    public TinyEventsBackgroundService(
        IServiceScopeFactory scopeFactory,
        TinyEventsWorkerOptions options)
        : this(scopeFactory, options, NullLogger<TinyEventsBackgroundService>.Instance)
    {
    }

    public TinyEventsBackgroundService(
        IServiceScopeFactory scopeFactory,
        TinyEventsWorkerOptions options,
        ILogger<TinyEventsBackgroundService> logger)
    {
        if (scopeFactory is null)
        {
            throw new ArgumentNullException(nameof(scopeFactory));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        this.scopeFactory = scopeFactory;
        this.options = options;
        this.logger = logger;
    }

    public async ValueTask ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();

        await processor.ProcessPendingAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = new TinyEventsWorkerFailureTracker();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);

                if (failures.TryReset(out var previousFailureCount))
                {
                    TinyEventsWorkerLog.Recovered(logger, previousFailureCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var failure = failures.RecordFailure();
                TinyEventsWorkerLog.IterationFailed(
                    logger,
                    failure,
                    exception);
            }

            try
            {
                await Task.Delay(options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
