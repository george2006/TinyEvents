using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinyEvents.Migrations.SqlServer;

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

        TinyEventsDatabaseProviderRegistrationGuard.EnsureCanRegister(
            services,
            TinyEventsDatabaseProviderRegistrationGuard.SqlServerAdoNet);

        var options = new TinyEventsSqlServerAdoNetOptions();
        configure(options);

        TinyEventsDatabaseProviderRegistrationGuard.Register(
            services,
            TinyEventsDatabaseProviderRegistrationGuard.SqlServerAdoNet);
        services.UseTinyEvents();
        services.TryAddSingleton(options);
        services.TryAddScoped<ITinySqlServerAdoNetWorkerConnectionFactory, TinySqlServerAdoNetWorkerConnectionFactory>();
        services.TryAddScoped<ISqlServerMigrationConnectionFactory, SqlServerAdoNetMigrationConnectionFactory>();
        services.TryAddScoped(serviceProvider =>
            new SqlServerTinyEventsMigrator(
                serviceProvider.GetRequiredService<ISqlServerMigrationConnectionFactory>(),
                options.TableName,
                serviceProvider.GetRequiredService<TimeProvider>()));
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinySqlServerAdoNetOutboxWriter>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxStore, TinySqlServerAdoNetOutboxStore>());

        return services;
    }
}
