using TinyEvents.Migrations.PostgreSql;
using TinyEvents.PostgreSql.AdoNet;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class PostgreSqlMigration001CreateOutboxTests
{
    [Fact]
    public void Migration_001_describes_the_initial_PostgreSQL_outbox()
    {
        var identity = PostgreSqlMigrationTableIdentity.Parse("TinyOutbox");

        var migration = PostgreSqlMigration001CreateOutbox.Create(identity);

        Assert.Equal(1, migration.Version);
        Assert.Equal("001_CreateTinyOutbox", migration.Name);
        Assert.Contains(
            "CREATE TABLE IF NOT EXISTS \"public\".\"TinyOutbox\"",
            migration.Sql);
        Assert.Contains(
            "CONSTRAINT \"PK_TinyOutbox\" PRIMARY KEY",
            migration.Sql);
        Assert.Contains(
            "CREATE INDEX IF NOT EXISTS \"IX_TinyOutbox_Pending\"",
            migration.Sql);
        Assert.Contains(
            "\"ClaimedAtUtc\" timestamp with time zone NULL",
            migration.Sql);
    }

    [Fact]
    public void Migration_001_derives_names_for_a_custom_outbox()
    {
        var identity =
            PostgreSqlMigrationTableIdentity.Parse("app.MyOutbox");

        var migration = PostgreSqlMigration001CreateOutbox.Create(identity);

        Assert.Contains(
            "CREATE TABLE IF NOT EXISTS \"app\".\"MyOutbox\"",
            migration.Sql);
        Assert.Contains("CONSTRAINT \"PK_MyOutbox\"", migration.Sql);
        Assert.Contains("\"IX_MyOutbox_Pending\"", migration.Sql);
        Assert.Contains("\"IX_MyOutbox_ExpiredProcessing\"", migration.Sql);
        Assert.Contains("\"IX_MyOutbox_ClaimedBy\"", migration.Sql);
    }

    [Fact]
    public void Public_schema_helper_returns_the_current_schema()
    {
        var identity =
            PostgreSqlMigrationTableIdentity.Parse("app.MyOutbox");
        var createOutbox = PostgreSqlMigration001CreateOutbox.Create(identity);
        var addCleanupIndex =
            PostgreSqlMigration002AddProcessedCleanupIndex.Create(identity);

        var helperSql =
            TinyPostgreSqlAdoNetSchema.CreateOutboxSql("app.MyOutbox");

        Assert.Contains(createOutbox.Sql, helperSql);
        Assert.Contains(addCleanupIndex.Sql, helperSql);
    }

    [Fact]
    public void Packed_default_SQL_asset_matches_migration_001()
    {
        var assetPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TinyEvents.PostgreSql.AdoNet",
            "Schema",
            "PostgreSql",
            "001_CreateTinyOutbox.sql");
        var assetSql = File.ReadAllText(assetPath);
        var migrationSql = PostgreSqlMigration001CreateOutbox
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
