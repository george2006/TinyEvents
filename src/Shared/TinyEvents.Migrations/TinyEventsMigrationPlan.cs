using System.Collections.ObjectModel;

namespace TinyEvents.Migrations;

internal sealed class TinyEventsMigrationPlan
{
    private readonly ReadOnlyCollection<TinyEventsMigration> pendingMigrations;

    internal TinyEventsMigrationPlan(
        long currentVersion,
        long targetVersion,
        IEnumerable<TinyEventsMigration> pendingMigrations)
    {
        ArgumentNullException.ThrowIfNull(pendingMigrations);

        CurrentVersion = currentVersion;
        TargetVersion = targetVersion;
        this.pendingMigrations = Array.AsReadOnly(pendingMigrations.ToArray());
    }

    internal long CurrentVersion { get; }

    internal long TargetVersion { get; }

    internal IReadOnlyList<TinyEventsMigration> PendingMigrations => pendingMigrations;

    internal bool IsCurrent => pendingMigrations.Count == 0;
}
