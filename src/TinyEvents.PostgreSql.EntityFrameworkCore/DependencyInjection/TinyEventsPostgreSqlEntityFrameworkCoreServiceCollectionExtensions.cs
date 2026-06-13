using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyEvents.PostgreSql.EntityFrameworkCore;

public static class TinyEventsPostgreSqlEntityFrameworkCoreServiceCollectionExtensions
{
    public static IServiceCollection UsePostgreSqlEntityFrameworkCoreOutbox<TDbContext>(
        this IServiceCollection services,
        Action<TinyEventsPostgreSqlEntityFrameworkCoreOptions>? configure = null)
        where TDbContext : DbContext
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        var options = new TinyEventsPostgreSqlEntityFrameworkCoreOptions();
        configure?.Invoke(options);

        services.UseTinyEvents();
        services.TryAddSingleton(options);
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinyPostgreSqlEfCoreOutboxWriter<TDbContext>>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxStore, TinyPostgreSqlEfCoreOutboxStore<TDbContext>>());

        return services;
    }
}
