using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TinyEvents.Worker;

public sealed class TinyEventsBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly TinyEventsWorkerOptions options;

    public TinyEventsBackgroundService(
        IServiceScopeFactory scopeFactory,
        TinyEventsWorkerOptions options)
    {
        if (scopeFactory is null)
        {
            throw new ArgumentNullException(nameof(scopeFactory));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        this.scopeFactory = scopeFactory;
        this.options = options;
    }

    public async ValueTask ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();

        await processor.ProcessPendingAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessOnceAsync(stoppingToken);
            await Task.Delay(options.PollingInterval, stoppingToken);
        }
    }
}

