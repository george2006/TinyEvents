using TinyEvents.Migrations.PostgreSql;

namespace TinyEvents.PostgreSql.AdoNet;

public static class TinyPostgreSqlAdoNetSchema
{
    public static string CreateOutboxSql(string tableName = "TinyOutbox")
    {
        var tableIdentity = PostgreSqlMigrationTableIdentity.Parse(tableName);
        return PostgreSqlMigration001CreateOutbox.Create(tableIdentity).Sql;
    }
}
