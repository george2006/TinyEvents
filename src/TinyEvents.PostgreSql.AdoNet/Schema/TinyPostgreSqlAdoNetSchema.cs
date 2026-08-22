using TinyEvents.Migrations.PostgreSql;

namespace TinyEvents.PostgreSql.AdoNet;

public static class TinyPostgreSqlAdoNetSchema
{
    public static string CreateOutboxSql(string tableName = "TinyOutbox")
    {
        var tableIdentity = PostgreSqlMigrationTableIdentity.Parse(tableName);
        var createOutbox = PostgreSqlMigration001CreateOutbox.Create(tableIdentity);
        var addCleanupIndex = PostgreSqlMigration002AddProcessedCleanupIndex.Create(tableIdentity);

        return $"{createOutbox.Sql}{Environment.NewLine}{addCleanupIndex.Sql}";
    }
}
