namespace TinyEvents;

public interface ITinyEventsMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken);
}
