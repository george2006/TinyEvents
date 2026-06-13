namespace TinyEvents.PostgreSql.AdoNet;

public static class TinyPostgreSqlAdoNetSchema
{
    public static string CreateOutboxSql(string tableName = "TinyOutbox")
    {
        var parsedTableName = TinyPostgreSqlAdoNetTableName.Parse(tableName);
        var postgreSqlName = parsedTableName.ToPostgreSqlName("public");
        var schemaName = parsedTableName.ToPostgreSqlSchemaName();

        return $$"""
            CREATE SCHEMA IF NOT EXISTS {{schemaName}};

            CREATE TABLE IF NOT EXISTS {{postgreSqlName}}
            (
                "Id" uuid NOT NULL CONSTRAINT "PK_TinyOutbox" PRIMARY KEY,
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

            CREATE INDEX IF NOT EXISTS "IX_TinyOutbox_Pending"
            ON {{postgreSqlName}}
            (
                "Status",
                "NextAttemptAtUtc",
                "CreatedAtUtc"
            );

            CREATE INDEX IF NOT EXISTS "IX_TinyOutbox_ExpiredProcessing"
            ON {{postgreSqlName}}
            (
                "Status",
                "ClaimExpiresAtUtc"
            );

            CREATE INDEX IF NOT EXISTS "IX_TinyOutbox_ClaimedBy"
            ON {{postgreSqlName}}
            (
                "ClaimedBy",
                "Status"
            );
            """;
    }
}
