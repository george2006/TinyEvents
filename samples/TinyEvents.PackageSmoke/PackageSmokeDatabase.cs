using System.Data.Common;
using Microsoft.Data.SqlClient;
using TinyEvents.SqlServer.AdoNet;

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
            await ExecuteAsync(connection, TinySqlServerAdoNetSchema.CreateOutboxSql());
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
}
