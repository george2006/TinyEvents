namespace TinyEvents.Migrations.SqlServer;

internal static class SqlServerMigration001CreateOutbox
{
    internal static TinyEventsMigration Create(SqlServerMigrationTableIdentity tableIdentity)
    {
        ArgumentNullException.ThrowIfNull(tableIdentity);

        return new TinyEventsMigration(
            1,
            "001_CreateTinyOutbox",
            CreateSql(tableIdentity));
    }

    private static string CreateSql(SqlServerMigrationTableIdentity tableIdentity)
    {
        var objectName = $"{tableIdentity.Schema}.{tableIdentity.OutboxTable}";

        return $$"""
            IF OBJECT_ID(N'{{objectName}}', N'U') IS NULL
            BEGIN
                CREATE TABLE {{tableIdentity.QuotedOutboxTable}}
                (
                    Id UNIQUEIDENTIFIER NOT NULL
                        CONSTRAINT {{tableIdentity.QuotedOutboxPrimaryKey}} PRIMARY KEY,
                    EventType NVARCHAR(512) NOT NULL,
                    Payload NVARCHAR(MAX) NOT NULL,
                    Status INT NOT NULL,
                    AttemptCount INT NOT NULL,
                    ClaimedBy NVARCHAR(256) NULL,
                    ClaimedAtUtc DATETIMEOFFSET NULL,
                    ClaimExpiresAtUtc DATETIMEOFFSET NULL,
                    CreatedAtUtc DATETIMEOFFSET NOT NULL,
                    NextAttemptAtUtc DATETIMEOFFSET NULL,
                    ProcessedAtUtc DATETIMEOFFSET NULL,
                    LastError NVARCHAR(MAX) NULL
                );
            END;

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'{{tableIdentity.PendingIndex}}'
                    AND object_id = OBJECT_ID(N'{{objectName}}')
            )
            BEGIN
                CREATE INDEX {{tableIdentity.QuotedPendingIndex}}
                ON {{tableIdentity.QuotedOutboxTable}}
                (
                    Status,
                    NextAttemptAtUtc,
                    CreatedAtUtc
                );
            END;

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'{{tableIdentity.ExpiredProcessingIndex}}'
                    AND object_id = OBJECT_ID(N'{{objectName}}')
            )
            BEGIN
                CREATE INDEX {{tableIdentity.QuotedExpiredProcessingIndex}}
                ON {{tableIdentity.QuotedOutboxTable}}
                (
                    Status,
                    ClaimExpiresAtUtc
                );
            END;

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'{{tableIdentity.ClaimedByIndex}}'
                    AND object_id = OBJECT_ID(N'{{objectName}}')
            )
            BEGIN
                CREATE INDEX {{tableIdentity.QuotedClaimedByIndex}}
                ON {{tableIdentity.QuotedOutboxTable}}
                (
                    ClaimedBy,
                    Status
                );
            END;
            """;
    }
}
