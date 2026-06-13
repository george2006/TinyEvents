namespace TinyEvents.PostgreSql.Tests;

public static class PostgreSqlSchema
{
    public const string CreateSchemaSql = """
        DROP TABLE IF EXISTS "Users";
        DROP TABLE IF EXISTS "TinyOutbox";

        CREATE TABLE "Users"
        (
            "Id" uuid NOT NULL PRIMARY KEY,
            "Email" text NOT NULL
        );

        CREATE TABLE "TinyOutbox"
        (
            "Id" uuid NOT NULL PRIMARY KEY,
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
        """;
}
