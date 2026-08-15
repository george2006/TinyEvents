using Microsoft.Extensions.DependencyInjection;

namespace TinyEvents;

internal sealed record TinyEventsDatabaseProviderRegistration(string ProviderIdentity);

internal static class TinyEventsDatabaseProviderRegistrationGuard
{
    internal const string SqlServerAdoNet = "SqlServer.AdoNet";
    internal const string SqlServerEntityFrameworkCore = "SqlServer.EntityFrameworkCore";
    internal const string PostgreSqlAdoNet = "PostgreSql.AdoNet";
    internal const string PostgreSqlEntityFrameworkCore = "PostgreSql.EntityFrameworkCore";

    internal static void EnsureCanRegister(
        IServiceCollection services,
        string requestedProviderIdentity)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedProviderIdentity);

        var existingRegistration = services
            .LastOrDefault(descriptor =>
                descriptor.ServiceType == typeof(TinyEventsDatabaseProviderRegistration))
            ?.ImplementationInstance as TinyEventsDatabaseProviderRegistration;

        if (existingRegistration is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"TinyEvents database provider '{existingRegistration.ProviderIdentity}' is already " +
            $"registered and '{requestedProviderIdentity}' cannot also be registered. " +
            "TinyEvents supports one database provider per service collection.");
    }

    internal static void Register(
        IServiceCollection services,
        string providerIdentity)
    {
        services.AddSingleton(
            new TinyEventsDatabaseProviderRegistration(providerIdentity));
    }
}
