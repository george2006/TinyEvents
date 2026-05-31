namespace TinyEvents.SqlServer.AdoNet;

public static class TinySqlServerAdoNetSchema
{
    public static string CreateOutboxSql(string tableName = "TinyOutbox")
    {
        var parsedTableName = TinySqlServerAdoNetTableName.Parse(tableName);
        var sqlServerName = parsedTableName.ToSqlServerName("dbo");
        var objectName = parsedTableName.ToSqlServerObjectName();

        return $$"""
            IF OBJECT_ID(N'{{objectName}}', N'U') IS NULL
            BEGIN
                CREATE TABLE {{sqlServerName}}
                (
                    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TinyOutbox PRIMARY KEY,
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
                WHERE name = N'IX_TinyOutbox_Pending'
                    AND object_id = OBJECT_ID(N'{{objectName}}')
            )
            BEGIN
                CREATE INDEX IX_TinyOutbox_Pending
                ON {{sqlServerName}}
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
                WHERE name = N'IX_TinyOutbox_ExpiredProcessing'
                    AND object_id = OBJECT_ID(N'{{objectName}}')
            )
            BEGIN
                CREATE INDEX IX_TinyOutbox_ExpiredProcessing
                ON {{sqlServerName}}
                (
                    Status,
                    ClaimExpiresAtUtc
                );
            END;

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_TinyOutbox_ClaimedBy'
                    AND object_id = OBJECT_ID(N'{{objectName}}')
            )
            BEGIN
                CREATE INDEX IX_TinyOutbox_ClaimedBy
                ON {{sqlServerName}}
                (
                    ClaimedBy,
                    Status
                );
            END;
            """;
    }
}
