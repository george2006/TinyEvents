using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyEvents;

public static class TinyEventsServiceCollectionExtensions
{
    public static IServiceCollection UseTinyEvents(
        this IServiceCollection services,
        Action<TinyEventsOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        RegisterCoreServices(services, configure);
        TinyEventsBootstrap.Apply(services);
        return services;
    }

    private static void RegisterCoreServices(
        IServiceCollection services,
        Action<TinyEventsOptions>? configure)
    {
        services.TryAddSingleton(TimeProvider.System);
        ConfigureOptions(services, configure);
        services.TryAddSingleton<ITinyEventSerializer, SystemTextJsonTinyEventSerializer>();
        services.TryAddScoped<ITinyEventPublisher, TinyEventPublisher>();
        services.TryAddScoped<ITinyOutboxProcessor, TinyOutboxProcessor>();
    }

    private static void ConfigureOptions(
        IServiceCollection services,
        Action<TinyEventsOptions>? configure)
    {
        var existingOptions = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(TinyEventsOptions))
            ?.ImplementationInstance as TinyEventsOptions;

        if (existingOptions is not null)
        {
            configure?.Invoke(existingOptions);
            return;
        }

        services.TryAddSingleton(CreateOptions(configure));
    }

    private static TinyEventsOptions CreateOptions(Action<TinyEventsOptions>? configure)
    {
        var options = new TinyEventsOptions();
        configure?.Invoke(options);
        return options;
    }
}
