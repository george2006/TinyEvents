using TinyEvents.Migrations.SqlServer;
using TinyEvents.SqlServer.AdoNet;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class SqlServerMigration001CreateOutboxTests
{
    [Fact]
    public void Migration_001_describes_the_initial_SQL_Server_outbox()
    {
        var identity = SqlServerMigrationTableIdentity.Parse("TinyOutbox");

        var migration = SqlServerMigration001CreateOutbox.Create(identity);

        Assert.Equal(1, migration.Version);
        Assert.Equal("001_CreateTinyOutbox", migration.Name);
        Assert.Contains("CREATE TABLE [dbo].[TinyOutbox]", migration.Sql);
        Assert.Contains("CONSTRAINT [PK_TinyOutbox] PRIMARY KEY", migration.Sql);
        Assert.Contains("CREATE INDEX [IX_TinyOutbox_Pending]", migration.Sql);
        Assert.Contains("CREATE INDEX [IX_TinyOutbox_ExpiredProcessing]", migration.Sql);
        Assert.Contains("CREATE INDEX [IX_TinyOutbox_ClaimedBy]", migration.Sql);
    }

    [Fact]
    public void Migration_001_derives_names_for_a_custom_outbox()
    {
        var identity = SqlServerMigrationTableIdentity.Parse("app.MyOutbox");

        var migration = SqlServerMigration001CreateOutbox.Create(identity);

        Assert.Contains("CREATE TABLE [app].[MyOutbox]", migration.Sql);
        Assert.Contains("CONSTRAINT [PK_MyOutbox] PRIMARY KEY", migration.Sql);
        Assert.Contains("CREATE INDEX [IX_MyOutbox_Pending]", migration.Sql);
        Assert.Contains("CREATE INDEX [IX_MyOutbox_ExpiredProcessing]", migration.Sql);
        Assert.Contains("CREATE INDEX [IX_MyOutbox_ClaimedBy]", migration.Sql);
    }

    [Fact]
    public void Public_schema_helper_returns_the_current_schema()
    {
        var identity = SqlServerMigrationTableIdentity.Parse("app.MyOutbox");
        var createOutbox = SqlServerMigration001CreateOutbox.Create(identity);
        var addCleanupIndex = SqlServerMigration002AddProcessedCleanupIndex.Create(identity);

        var helperSql = TinySqlServerAdoNetSchema.CreateOutboxSql("app.MyOutbox");

        Assert.Contains(createOutbox.Sql, helperSql);
        Assert.Contains(addCleanupIndex.Sql, helperSql);
    }

    [Fact]
    public void Packed_default_SQL_asset_matches_migration_001()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assetPath = Path.Combine(
            repositoryRoot,
            "src",
            "TinyEvents.SqlServer.AdoNet",
            "Schema",
            "SqlServer",
            "001_CreateTinyOutbox.sql");
        var assetSql = File.ReadAllText(assetPath);
        var migrationSql = SqlServerMigration001CreateOutbox
            .Create(SqlServerMigrationTableIdentity.Parse("TinyOutbox"))
            .Sql;

        Assert.Equal(NormalizeLineEndings(migrationSql), NormalizeLineEndings(assetSql).TrimEnd());
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

        throw new InvalidOperationException("Could not find the TinyEvents repository root.");
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
