using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyEvents.SqlServer.AdoNet;

public static class TinyEventsSqlServerAdoNetServiceCollectionExtensions
{
    public static IServiceCollection UseSqlServerAdoNetOutbox(
        this IServiceCollection services,
        Action<TinyEventsSqlServerAdoNetOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new TinyEventsSqlServerAdoNetOptions();
        configure(options);

        services.UseTinyEvents();
        services.TryAddSingleton(options);
        services.TryAddScoped<ITinySqlServerAdoNetWorkerConnectionFactory, TinySqlServerAdoNetWorkerConnectionFactory>();
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinySqlServerAdoNetOutboxWriter>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxStore, TinySqlServerAdoNetOutboxStore>());

        return services;
    }
}
