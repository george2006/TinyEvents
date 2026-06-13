using System.Data.Common;
using Npgsql;
using TinyEvents.PostgreSql.AdoNet;

namespace TinyEvents.Sample.PostgreSql.AdoNet.Infrastructure;

internal static class PostgreSqlSchema
{
    public static async ValueTask EnsureCreatedAsync(string connectionString)
    {
        await EnsureDatabaseCreatedAsync(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, CreateUsersTableSql);
        await ExecuteAsync(connection, TinyPostgreSqlAdoNetSchema.CreateOutboxSql());
    }

    private static async ValueTask EnsureDatabaseCreatedAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        if (await DatabaseExistsAsync(connection, databaseName))
        {
            return;
        }

        await ExecuteAsync(connection, $"""CREATE DATABASE "{databaseName.Replace("\"", "\"\"")}";""");
    }

    private static async ValueTask<bool> DatabaseExistsAsync(
        DbConnection connection,
        string databaseName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM pg_database WHERE datname = @DatabaseName;";
        command.AddParameter("@DatabaseName", databaseName);
        var result = await command.ExecuteScalarAsync();
        return result is not null;
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
        CREATE TABLE IF NOT EXISTS "Users"
        (
            "Id" uuid NOT NULL PRIMARY KEY,
            "Email" text NOT NULL
        );
        """;
}
