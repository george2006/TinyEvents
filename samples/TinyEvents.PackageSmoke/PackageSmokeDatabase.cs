using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace TinyEvents.PackageSmoke;

internal static class PackageSmokeDatabase
{
    public static async ValueTask EnsureCreatedAsync(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using (var connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"""
                IF DB_ID(N'{databaseName.Replace("'", "''")}') IS NULL
                BEGIN
                    CREATE DATABASE [{databaseName.Replace("]", "]]")}];
                END;
                """);
        }

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, CreateOutboxTableSql);
        }
    }

    public static async ValueTask ResetOutboxAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM dbo.TinyOutbox;");
    }

    private static async ValueTask ExecuteAsync(
        DbConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    // Keep this local because the sample is pinned to the already-published package version.
    // Newer ADO.NET packages expose TinySqlServerAdoNetSchema.CreateOutboxSql().
    private const string CreateOutboxTableSql = """
        IF OBJECT_ID(N'dbo.TinyOutbox', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.TinyOutbox
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
                AND object_id = OBJECT_ID(N'dbo.TinyOutbox')
        )
        BEGIN
            CREATE INDEX IX_TinyOutbox_Pending
            ON dbo.TinyOutbox
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
                AND object_id = OBJECT_ID(N'dbo.TinyOutbox')
        )
        BEGIN
            CREATE INDEX IX_TinyOutbox_ExpiredProcessing
            ON dbo.TinyOutbox
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
                AND object_id = OBJECT_ID(N'dbo.TinyOutbox')
        )
        BEGIN
            CREATE INDEX IX_TinyOutbox_ClaimedBy
            ON dbo.TinyOutbox
            (
                ClaimedBy,
                Status
            );
        END;
        """;
}
