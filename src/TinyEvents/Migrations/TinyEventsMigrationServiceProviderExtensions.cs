using Microsoft.Extensions.DependencyInjection;

namespace TinyEvents;

public static class TinyEventsMigrationServiceProviderExtensions
{
    private const string MissingProviderMessage =
        "No TinyEvents database provider is registered. Register exactly one " +
        "TinyEvents database provider before calling MigrateTinyEventsAsync.";

    public static async Task MigrateTinyEventsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetService<ITinyEventsMigrator>();

        if (migrator is null)
        {
            throw new InvalidOperationException(MissingProviderMessage);
        }

        await migrator.MigrateAsync(cancellationToken);
    }
}
