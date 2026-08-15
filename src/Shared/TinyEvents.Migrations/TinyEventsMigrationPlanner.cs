namespace TinyEvents.Migrations;

internal sealed class TinyEventsMigrationPlanner
{
    internal TinyEventsMigrationPlan CreatePlan(
        TinyEventsMigrationCatalog catalog,
        IEnumerable<AppliedTinyEventsMigration> appliedMigrations)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(appliedMigrations);

        var orderedAppliedMigrations = appliedMigrations
            .OrderBy(migration => migration?.Version)
            .ToArray();

        if (orderedAppliedMigrations.Any(migration => migration is null))
        {
            throw new ArgumentException(
                "Applied migration history cannot contain a null migration.",
                nameof(appliedMigrations));
        }

        if (orderedAppliedMigrations.Length == 0)
        {
            return NewPlan(catalog, 0);
        }

        var databaseVersion = orderedAppliedMigrations[^1].Version;

        if (databaseVersion > catalog.LatestVersion)
        {
            throw new InvalidOperationException(
                $"The database migration version {databaseVersion} is newer than " +
                $"the provider migration version {catalog.LatestVersion}.");
        }

        EnsureHistoryIsContiguous(orderedAppliedMigrations);
        EnsureHistoryMatchesCatalog(catalog, orderedAppliedMigrations);

        return NewPlan(catalog, databaseVersion);
    }

    private static void EnsureHistoryIsContiguous(
        IReadOnlyList<AppliedTinyEventsMigration> appliedMigrations)
    {
        for (var index = 0; index < appliedMigrations.Count; index++)
        {
            var expectedVersion = index + 1L;
            var actualVersion = appliedMigrations[index].Version;

            if (actualVersion != expectedVersion)
            {
                throw new InvalidOperationException(
                    $"Applied migration history is not contiguous. Expected version " +
                    $"{expectedVersion}, but found version {actualVersion}.");
            }
        }
    }

    private static void EnsureHistoryMatchesCatalog(
        TinyEventsMigrationCatalog catalog,
        IReadOnlyList<AppliedTinyEventsMigration> appliedMigrations)
    {
        foreach (var appliedMigration in appliedMigrations)
        {
            var catalogMigration = catalog.Migrations[(int)appliedMigration.Version - 1];

            if (!string.Equals(
                    appliedMigration.Name,
                    catalogMigration.Name,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Applied migration version {appliedMigration.Version} is named " +
                    $"'{appliedMigration.Name}', but the provider expects " +
                    $"'{catalogMigration.Name}'.");
            }

            if (!string.Equals(
                    appliedMigration.Checksum,
                    catalogMigration.Checksum,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Applied migration version {appliedMigration.Version} has a checksum " +
                    "that does not match the provider migration.");
            }
        }
    }

    private static TinyEventsMigrationPlan NewPlan(
        TinyEventsMigrationCatalog catalog,
        long currentVersion)
    {
        var pendingMigrations = catalog.Migrations
            .Skip((int)currentVersion);

        return new TinyEventsMigrationPlan(
            currentVersion,
            catalog.LatestVersion,
            pendingMigrations);
    }
}
