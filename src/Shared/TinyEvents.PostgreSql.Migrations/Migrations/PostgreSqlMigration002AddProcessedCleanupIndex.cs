namespace TinyEvents.Migrations.PostgreSql;

internal static class PostgreSqlMigration002AddProcessedCleanupIndex
{
    internal static TinyEventsMigration Create(
        PostgreSqlMigrationTableIdentity tableIdentity)
    {
        ArgumentNullException.ThrowIfNull(tableIdentity);

        return new TinyEventsMigration(
            2,
            "002_AddProcessedCleanupIndex",
            CreateSql(tableIdentity));
    }

    private static string CreateSql(
        PostgreSqlMigrationTableIdentity tableIdentity)
    {
        return $$"""
            CREATE INDEX IF NOT EXISTS {{tableIdentity.QuotedProcessedCleanupIndex}}
            ON {{tableIdentity.QuotedOutboxTable}}
            (
                "Status",
                "ProcessedAtUtc",
                "Id"
            );
            """;
    }
}
