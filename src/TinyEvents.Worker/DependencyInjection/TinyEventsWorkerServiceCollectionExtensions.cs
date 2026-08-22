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

        var workerOptions = ConfigureOptions(services, configure);
        services.TryAddScoped<TinyOutboxCleanup>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TinyEventsBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TinyEventsCleanupBackgroundService>());
        services.ConfigureTinyEventsForWorker(workerOptions);

        return services;
    }

    private static TinyEventsWorkerOptions ConfigureOptions(
        IServiceCollection services,
        Action<TinyEventsWorkerOptions>? configure)
    {
        var existingOptions = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(TinyEventsWorkerOptions))
            ?.ImplementationInstance as TinyEventsWorkerOptions;

        if (existingOptions is not null)
        {
            configure?.Invoke(existingOptions);
            return existingOptions;
        }

        var options = CreateOptions(configure);
        services.TryAddSingleton(options);
        return options;
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
    }
}
