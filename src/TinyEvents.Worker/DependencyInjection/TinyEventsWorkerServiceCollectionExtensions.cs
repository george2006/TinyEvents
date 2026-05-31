using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace TinyEvents.Worker;

public static class TinyEventsWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddTinyEventsWorker(
        this IServiceCollection services,
        Action<TinyEventsWorkerOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        var workerOptions = CreateOptions(configure);
        services.TryAddSingleton(workerOptions);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TinyEventsBackgroundService>());
        services.ConfigureTinyEventsForWorker(workerOptions);

        return services;
    }

    private static TinyEventsWorkerOptions CreateOptions(Action<TinyEventsWorkerOptions>? configure)
    {
        var options = new TinyEventsWorkerOptions();
        configure?.Invoke(options);
        return options;
    }

    private static void ConfigureTinyEventsForWorker(
        this IServiceCollection services,
        TinyEventsWorkerOptions workerOptions)
    {
        services.UseTinyEvents(options =>
        {
            options.WorkerId = workerOptions.WorkerId;
            options.BatchSize = workerOptions.BatchSize;
            options.ClaimTimeout = workerOptions.ClaimTimeout;
        });

        services.Replace(ServiceDescriptor.Singleton(CreateCoreOptions(workerOptions)));
    }

    private static TinyEventsOptions CreateCoreOptions(TinyEventsWorkerOptions workerOptions)
    {
        return new TinyEventsOptions
        {
            WorkerId = workerOptions.WorkerId,
            BatchSize = workerOptions.BatchSize,
            ClaimTimeout = workerOptions.ClaimTimeout
        };
    }
}
