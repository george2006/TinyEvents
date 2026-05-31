namespace TinyEvents.SqlServer.Tests;

public static class SqlServerSchema
{
    public const string CreateSchemaSql = """
        IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
        BEGIN
            DROP TABLE dbo.Users;
        END;

        IF OBJECT_ID(N'dbo.TinyOutbox', N'U') IS NOT NULL
        BEGIN
            DROP TABLE dbo.TinyOutbox;
        END;

        CREATE TABLE dbo.Users
        (
            Id uniqueidentifier NOT NULL PRIMARY KEY,
            Email nvarchar(320) NOT NULL
        );

        CREATE TABLE dbo.TinyOutbox
        (
            Id uniqueidentifier NOT NULL PRIMARY KEY,
            EventType nvarchar(512) NOT NULL,
            Payload nvarchar(max) NOT NULL,
            Status int NOT NULL,
            AttemptCount int NOT NULL,
            ClaimedBy nvarchar(256) NULL,
            ClaimedAtUtc datetimeoffset NULL,
            ClaimExpiresAtUtc datetimeoffset NULL,
            CreatedAtUtc datetimeoffset NOT NULL,
            NextAttemptAtUtc datetimeoffset NULL,
            ProcessedAtUtc datetimeoffset NULL,
            LastError nvarchar(max) NULL
        );
        """;
}

