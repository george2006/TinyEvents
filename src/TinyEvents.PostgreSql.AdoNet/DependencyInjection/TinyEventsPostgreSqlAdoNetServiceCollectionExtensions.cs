using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinyEvents.Migrations.PostgreSql;

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

        TinyEventsDatabaseProviderRegistrationGuard.EnsureCanRegister(
            services,
            TinyEventsDatabaseProviderRegistrationGuard.PostgreSqlAdoNet);

        var options = new TinyEventsPostgreSqlAdoNetOptions();
        configure(options);

        TinyEventsDatabaseProviderRegistrationGuard.Register(
            services,
            TinyEventsDatabaseProviderRegistrationGuard.PostgreSqlAdoNet);
        services.UseTinyEvents();
        services.TryAddSingleton(options);
        services.TryAddScoped<ITinyPostgreSqlAdoNetWorkerConnectionFactory, TinyPostgreSqlAdoNetWorkerConnectionFactory>();
        services.TryAddScoped<IPostgreSqlMigrationConnectionFactory, PostgreSqlAdoNetMigrationConnectionFactory>();
        services.TryAddScoped(serviceProvider =>
            new PostgreSqlTinyEventsMigrator(
                serviceProvider.GetRequiredService<IPostgreSqlMigrationConnectionFactory>(),
                options.TableName,
                serviceProvider.GetRequiredService<TimeProvider>()));
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinyPostgreSqlAdoNetOutboxWriter>());
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxStore, TinyPostgreSqlAdoNetOutboxStore>());

        return services;
    }
}
