using System.Collections.ObjectModel;

namespace TinyEvents.Migrations;

internal sealed class TinyEventsMigrationCatalog
{
    private readonly ReadOnlyCollection<TinyEventsMigration> migrations;

    internal TinyEventsMigrationCatalog(IEnumerable<TinyEventsMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        var orderedMigrations = migrations
            .OrderBy(migration => migration?.Version)
            .ToArray();

        if (orderedMigrations.Length == 0)
        {
            throw new ArgumentException(
                "A migration catalog must contain at least migration version 1.",
                nameof(migrations));
        }

        EnsureEntriesAreValid(orderedMigrations, nameof(migrations));
        EnsureVersionsAreUnique(orderedMigrations, nameof(migrations));
        EnsureNamesAreUnique(orderedMigrations, nameof(migrations));
        EnsureVersionsAreContiguous(orderedMigrations, nameof(migrations));

        this.migrations = Array.AsReadOnly(orderedMigrations);
    }

    internal IReadOnlyList<TinyEventsMigration> Migrations => migrations;

    internal long LatestVersion => migrations[^1].Version;

    private static void EnsureEntriesAreValid(
        IReadOnlyList<TinyEventsMigration> migrations,
        string parameterName)
    {
        if (migrations.Any(migration => migration is null))
        {
            throw new ArgumentException(
                "A migration catalog cannot contain a null migration.",
                parameterName);
        }
    }

    private static void EnsureVersionsAreUnique(
        IReadOnlyList<TinyEventsMigration> migrations,
        string parameterName)
    {
        var versions = new HashSet<long>();

        foreach (var migration in migrations)
        {
            if (!versions.Add(migration.Version))
            {
                throw new ArgumentException(
                    $"Migration version {migration.Version} appears more than once.",
                    parameterName);
            }
        }
    }

    private static void EnsureNamesAreUnique(
        IReadOnlyList<TinyEventsMigration> migrations,
        string parameterName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var migration in migrations)
        {
            if (!names.Add(migration.Name))
            {
                throw new ArgumentException(
                    $"Migration name '{migration.Name}' appears more than once.",
                    parameterName);
            }
        }
    }

    private static void EnsureVersionsAreContiguous(
        IReadOnlyList<TinyEventsMigration> migrations,
        string parameterName)
    {
        for (var index = 0; index < migrations.Count; index++)
        {
            var expectedVersion = index + 1L;
            var actualVersion = migrations[index].Version;

            if (actualVersion != expectedVersion)
            {
                throw new ArgumentException(
                    $"Expected migration version {expectedVersion}, but found version {actualVersion}.",
                    parameterName);
            }
        }
    }
}
