using TinyEvents.Migrations.SqlServer;

namespace TinyEvents.SqlServer.AdoNet;

public static class TinySqlServerAdoNetSchema
{
    public static string CreateOutboxSql(string tableName = "TinyOutbox")
    {
        var tableIdentity = SqlServerMigrationTableIdentity.Parse(tableName);
        var createOutbox = SqlServerMigration001CreateOutbox.Create(tableIdentity);
        var addCleanupIndex = SqlServerMigration002AddProcessedCleanupIndex.Create(tableIdentity);

        return $"{createOutbox.Sql}{Environment.NewLine}{addCleanupIndex.Sql}";
    }
}
