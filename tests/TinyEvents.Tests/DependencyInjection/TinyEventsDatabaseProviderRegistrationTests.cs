using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents.PostgreSql.AdoNet;
using TinyEvents.PostgreSql.EntityFrameworkCore;
using TinyEvents.SqlServer.AdoNet;
using TinyEvents.SqlServer.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.Tests.DependencyInjection;

public sealed class TinyEventsDatabaseProviderRegistrationTests
{
    public static IEnumerable<object[]> ProviderIdentities()
    {
        yield return ["SqlServer.AdoNet"];
        yield return ["SqlServer.EntityFrameworkCore"];
        yield return ["PostgreSql.AdoNet"];
        yield return ["PostgreSql.EntityFrameworkCore"];
    }

    public static IEnumerable<object[]> ConflictingProviderIdentities()
    {
        var providers = ProviderIdentities()
            .Select(values => (string)values[0])
            .ToArray();

        foreach (var existingProvider in providers)
        {
            foreach (var requestedProvider in providers)
            {
                if (existingProvider != requestedProvider)
                {
                    yield return [existingProvider, requestedProvider];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProviderIdentities))]
    public void Provider_registers_the_public_scoped_migrator_contract(
        string providerIdentity)
    {
        var services = new ServiceCollection();

        Register(services, providerIdentity);

        var descriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(ITinyEventsMigrator));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Theory]
    [MemberData(nameof(ProviderIdentities))]
    public void Repeating_a_provider_is_rejected_before_configuration_or_mutation(
        string providerIdentity)
    {
        var services = new ServiceCollection();
        Register(services, providerIdentity);
        var descriptorsBeforeRejection = services.ToArray();
        var rejectedConfigurationCalls = 0;

        var exception = Assert.Throws<InvalidOperationException>(
            () => Register(
                services,
                providerIdentity,
                () => rejectedConfigurationCalls++));

        Assert.Equal(0, rejectedConfigurationCalls);
        Assert.Equal(descriptorsBeforeRejection, services);
        Assert.Contains(providerIdentity, exception.Message);
        Assert.Contains("one database provider per service collection", exception.Message);
    }

    [Theory]
    [MemberData(nameof(ConflictingProviderIdentities))]
    public void Conflicting_providers_are_rejected_before_configuration_or_mutation(
        string existingProvider,
        string requestedProvider)
    {
        var services = new ServiceCollection();
        Register(services, existingProvider);
        var descriptorsBeforeRejection = services.ToArray();
        var rejectedConfigurationCalls = 0;

        var exception = Assert.Throws<InvalidOperationException>(
            () => Register(
                services,
                requestedProvider,
                () => rejectedConfigurationCalls++));

        Assert.Equal(0, rejectedConfigurationCalls);
        Assert.Equal(descriptorsBeforeRejection, services);
        Assert.Contains(existingProvider, exception.Message);
        Assert.Contains(requestedProvider, exception.Message);
    }

    [Fact]
    public void Rejected_registration_leaves_the_original_SQL_Server_ADO_NET_provider_usable()
    {
        var services = new ServiceCollection();
        services.UseSqlServerAdoNetOutbox(options =>
        {
            options.UseWorkerConnectionFactory(
                (_, _) => new ValueTask<DbConnection>((DbConnection)null!));
        });
        var migratorServiceType = Assert.Single(
            services,
            descriptor => descriptor.ServiceType.Name == "SqlServerTinyEventsMigrator")
            .ServiceType;

        Assert.Throws<InvalidOperationException>(
            () => services.UsePostgreSqlEntityFrameworkCoreOutbox<TestDbContext>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITinyOutboxWriter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>());
        Assert.NotNull(provider.GetRequiredService<TinyEventsSqlServerAdoNetOptions>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService(migratorServiceType));
    }

    private static void Register(
        IServiceCollection services,
        string providerIdentity,
        Action? configuration = null)
    {
        switch (providerIdentity)
        {
            case "SqlServer.AdoNet":
                services.UseSqlServerAdoNetOutbox(_ => configuration?.Invoke());
                return;

            case "SqlServer.EntityFrameworkCore":
                services.UseSqlServerEntityFrameworkCoreOutbox<TestDbContext>(
                    _ => configuration?.Invoke());
                return;

            case "PostgreSql.AdoNet":
                services.UsePostgreSqlAdoNetOutbox(_ => configuration?.Invoke());
                return;

            case "PostgreSql.EntityFrameworkCore":
                services.UsePostgreSqlEntityFrameworkCoreOutbox<TestDbContext>(
                    _ => configuration?.Invoke());
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(providerIdentity),
                    providerIdentity,
                    "Unknown test provider.");
        }
    }

    private sealed class TestDbContext : DbContext;
}
