namespace TinyEvents.Migrations.SqlServer;

internal static class SqlServerMigration002AddProcessedCleanupIndex
{
    internal static TinyEventsMigration Create(SqlServerMigrationTableIdentity tableIdentity)
    {
        ArgumentNullException.ThrowIfNull(tableIdentity);

        return new TinyEventsMigration(
            2,
            "002_AddProcessedCleanupIndex",
            CreateSql(tableIdentity));
    }

    private static string CreateSql(SqlServerMigrationTableIdentity tableIdentity)
    {
        var objectName = $"{tableIdentity.Schema}.{tableIdentity.OutboxTable}";

        return $$"""
            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'{{tableIdentity.ProcessedCleanupIndex}}'
                    AND object_id = OBJECT_ID(N'{{objectName}}')
            )
            BEGIN
                CREATE INDEX {{tableIdentity.QuotedProcessedCleanupIndex}}
                ON {{tableIdentity.QuotedOutboxTable}}
                (
                    Status,
                    ProcessedAtUtc,
                    Id
                );
            END;
            """;
    }
}
