using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyEvents.SqlServer.EntityFrameworkCore;

public static class TinyEventsSqlServerEntityFrameworkCoreServiceCollectionExtensions
{
    public static IServiceCollection UseSqlServerEntityFrameworkCoreOutbox<TDbContext>(
        this IServiceCollection services,
        Action<TinyEventsSqlServerEntityFrameworkCoreOptions>? configure = null)
        where TDbContext : DbContext
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        var options = new TinyEventsSqlServerEntityFrameworkCoreOptions();
        configure?.Invoke(options);

        services.UseTinyEvents();
        services.TryAddSingleton(options);
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinySqlServerEfCoreOutboxWriter<TDbContext>>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxStore, TinySqlServerEfCoreOutboxStore<TDbContext>>());

        return services;
    }
}
