using TinyEvents.Migrations.PostgreSql;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class PostgreSqlMigration002AddProcessedCleanupIndexTests
{
    [Fact]
    public void Migration_002_adds_the_processed_cleanup_index()
    {
        var identity = PostgreSqlMigrationTableIdentity.Parse("app.MyOutbox");

        var migration = PostgreSqlMigration002AddProcessedCleanupIndex.Create(identity);

        Assert.Equal(2, migration.Version);
        Assert.Equal("002_AddProcessedCleanupIndex", migration.Name);
        Assert.Contains(
            "CREATE INDEX IF NOT EXISTS \"IX_MyOutbox_ProcessedCleanup\"",
            migration.Sql);
        Assert.Contains("\"Status\",", migration.Sql);
        Assert.Contains("\"ProcessedAtUtc\",", migration.Sql);
        Assert.Contains("\"Id\"", migration.Sql);
    }

    [Fact]
    public void Packed_default_SQL_asset_matches_migration_002()
    {
        var assetPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TinyEvents.PostgreSql.AdoNet",
            "Schema",
            "PostgreSql",
            "002_AddProcessedCleanupIndex.sql");
        var assetSql = File.ReadAllText(assetPath);
        var migrationSql = PostgreSqlMigration002AddProcessedCleanupIndex
            .Create(PostgreSqlMigrationTableIdentity.Parse("TinyOutbox"))
            .Sql;

        Assert.Equal(
            NormalizeLineEndings(migrationSql),
            NormalizeLineEndings(assetSql).TrimEnd());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TinyEvents.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find the TinyEvents repository root.");
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
