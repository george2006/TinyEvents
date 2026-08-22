using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TinyEvents.Migrations.SqlServer;

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

        TinyEventsDatabaseProviderRegistrationGuard.EnsureCanRegister(
            services,
            TinyEventsDatabaseProviderRegistrationGuard.SqlServerEntityFrameworkCore);

        var options = new TinyEventsSqlServerEntityFrameworkCoreOptions();
        configure?.Invoke(options);

        TinyEventsDatabaseProviderRegistrationGuard.Register(
            services,
            TinyEventsDatabaseProviderRegistrationGuard.SqlServerEntityFrameworkCore);
        services.UseTinyEvents();
        services.TryAddSingleton(options);
        services.TryAddScoped<
            ISqlServerMigrationConnectionFactory,
            SqlServerEfCoreMigrationConnectionFactory<TDbContext>>();
        services.TryAddScoped(serviceProvider =>
            new SqlServerTinyEventsMigrator(
                serviceProvider.GetRequiredService<ISqlServerMigrationConnectionFactory>(),
                options.TableName,
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetService<ILogger<SqlServerTinyEventsMigrator>>()));
        services.TryAddScoped<ITinyEventsMigrator>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlServerTinyEventsMigrator>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinySqlServerEfCoreOutboxWriter<TDbContext>>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxStore, TinySqlServerEfCoreOutboxStore<TDbContext>>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxCleanupStore, TinySqlServerEfCoreOutboxStore<TDbContext>>());

        return services;
    }
}
