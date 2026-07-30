namespace TinyEvents.Migrations.PostgreSql;

internal static class PostgreSqlMigration001CreateOutbox
{
    internal static TinyEventsMigration Create(
        PostgreSqlMigrationTableIdentity tableIdentity)
    {
        ArgumentNullException.ThrowIfNull(tableIdentity);

        return new TinyEventsMigration(
            1,
            "001_CreateTinyOutbox",
            CreateSql(tableIdentity));
    }

    private static string CreateSql(
        PostgreSqlMigrationTableIdentity tableIdentity)
    {
        return $$"""
            CREATE SCHEMA IF NOT EXISTS {{tableIdentity.QuotedSchema}};

            CREATE TABLE IF NOT EXISTS {{tableIdentity.QuotedOutboxTable}}
            (
                "Id" uuid NOT NULL
                    CONSTRAINT {{tableIdentity.QuotedOutboxPrimaryKey}} PRIMARY KEY,
                "EventType" text NOT NULL,
                "Payload" text NOT NULL,
                "Status" integer NOT NULL,
                "AttemptCount" integer NOT NULL,
                "ClaimedBy" text NULL,
                "ClaimedAtUtc" timestamp with time zone NULL,
                "ClaimExpiresAtUtc" timestamp with time zone NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "NextAttemptAtUtc" timestamp with time zone NULL,
                "ProcessedAtUtc" timestamp with time zone NULL,
                "LastError" text NULL
            );

            CREATE INDEX IF NOT EXISTS {{tableIdentity.QuotedPendingIndex}}
            ON {{tableIdentity.QuotedOutboxTable}}
            (
                "Status",
                "NextAttemptAtUtc",
                "CreatedAtUtc"
            );

            CREATE INDEX IF NOT EXISTS {{tableIdentity.QuotedExpiredProcessingIndex}}
            ON {{tableIdentity.QuotedOutboxTable}}
            (
                "Status",
                "ClaimExpiresAtUtc"
            );

            CREATE INDEX IF NOT EXISTS {{tableIdentity.QuotedClaimedByIndex}}
            ON {{tableIdentity.QuotedOutboxTable}}
            (
                "ClaimedBy",
                "Status"
            );
            """;
    }
}
