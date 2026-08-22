using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TinyEvents.Migrations.PostgreSql;

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

        TinyEventsDatabaseProviderRegistrationGuard.EnsureCanRegister(
            services,
            TinyEventsDatabaseProviderRegistrationGuard.PostgreSqlEntityFrameworkCore);

        var options = new TinyEventsPostgreSqlEntityFrameworkCoreOptions();
        configure?.Invoke(options);

        TinyEventsDatabaseProviderRegistrationGuard.Register(
            services,
            TinyEventsDatabaseProviderRegistrationGuard.PostgreSqlEntityFrameworkCore);
        services.UseTinyEvents();
        services.TryAddSingleton(options);
        services.TryAddScoped<
            IPostgreSqlMigrationConnectionFactory,
            PostgreSqlEfCoreMigrationConnectionFactory<TDbContext>>();
        services.TryAddScoped(serviceProvider =>
            new PostgreSqlTinyEventsMigrator(
                serviceProvider.GetRequiredService<IPostgreSqlMigrationConnectionFactory>(),
                options.TableName,
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetService<ILogger<PostgreSqlTinyEventsMigrator>>()));
        services.TryAddScoped<ITinyEventsMigrator>(serviceProvider =>
            serviceProvider.GetRequiredService<PostgreSqlTinyEventsMigrator>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinyPostgreSqlEfCoreOutboxWriter<TDbContext>>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxStore, TinyPostgreSqlEfCoreOutboxStore<TDbContext>>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxCleanupStore, TinyPostgreSqlEfCoreOutboxStore<TDbContext>>());

        return services;
    }
}
