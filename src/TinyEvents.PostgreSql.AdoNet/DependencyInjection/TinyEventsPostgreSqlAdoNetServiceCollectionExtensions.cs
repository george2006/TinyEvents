using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyEvents.PostgreSql.AdoNet;

public static class TinyEventsPostgreSqlAdoNetServiceCollectionExtensions
{
    public static IServiceCollection UsePostgreSqlAdoNetOutbox(
        this IServiceCollection services,
        Action<TinyEventsPostgreSqlAdoNetOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new TinyEventsPostgreSqlAdoNetOptions();
        configure(options);

        services.UseTinyEvents();
        services.TryAddSingleton(options);
        services.TryAddScoped<ITinyPostgreSqlAdoNetWorkerConnectionFactory, TinyPostgreSqlAdoNetWorkerConnectionFactory>();
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinyPostgreSqlAdoNetOutboxWriter>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxStore, TinyPostgreSqlAdoNetOutboxStore>());

        return services;
    }
}
