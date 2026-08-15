using TinyEvents.Migrations.SqlServer;

namespace TinyEvents.SqlServer.AdoNet;

public static class TinySqlServerAdoNetSchema
{
    public static string CreateOutboxSql(string tableName = "TinyOutbox")
    {
        var tableIdentity = SqlServerMigrationTableIdentity.Parse(tableName);
        return SqlServerMigration001CreateOutbox.Create(tableIdentity).Sql;
    }
}
