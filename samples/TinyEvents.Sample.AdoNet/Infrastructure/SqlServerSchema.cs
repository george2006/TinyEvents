using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace TinyEvents.Sample.AdoNet.Infrastructure;

internal static class SqlServerSchema
{
    public static async ValueTask EnsureCreatedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, CreateUsersTableSql);
        await ExecuteAsync(connection, CreateOutboxTableSql);
    }

    private static async ValueTask ExecuteAsync(
        DbConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private const string CreateUsersTableSql = """
        IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Users
            (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                Email NVARCHAR(320) NOT NULL
            );
        END;
        """;

    private const string CreateOutboxTableSql = """
        IF OBJECT_ID(N'dbo.TinyOutbox', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.TinyOutbox
            (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
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
        """;
}
