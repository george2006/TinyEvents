using Microsoft.Data.SqlClient;
using TinyEvents.Migrations.SqlServer;
using Xunit;

namespace TinyEvents.SqlServer.Tests;

public sealed class SqlServerMigration001Tests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public SqlServerMigration001Tests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [SqlServerIntegrationFact]
    public async Task Migration_001_creates_independent_outboxes_and_is_safe_to_repeat()
    {
        const string schema = "migration_001";
        await using var connection = await OpenConnectionAsync();
        await ResetSchemaAsync(connection, schema);
        var firstIdentity = SqlServerMigrationTableIdentity.Parse($"{schema}.FirstOutbox");
        var secondIdentity = SqlServerMigrationTableIdentity.Parse($"{schema}.SecondOutbox");
        var history = new SqlServerMigrationHistory(firstIdentity, TimeProvider.System);
        await history.EnsureSchemaAsync(connection, CancellationToken.None);
        var firstMigration = SqlServerMigration001CreateOutbox.Create(firstIdentity);
        var secondMigration = SqlServerMigration001CreateOutbox.Create(secondIdentity);

        await ExecuteAsync(connection, firstMigration.Sql);
        await ExecuteAsync(connection, firstMigration.Sql);
        await ExecuteAsync(connection, secondMigration.Sql);

        Assert.Equal(2, await OutboxTableCountAsync(connection, schema));
        Assert.Equal(6, await OutboxIndexCountAsync(connection, schema));
        Assert.Equal(12, await ColumnCountAsync(connection, schema, "FirstOutbox"));
        Assert.Equal(12, await ColumnCountAsync(connection, schema, "SecondOutbox"));
    }

    private async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ResetSchemaAsync(
        SqlConnection connection,
        string schema)
    {
        await ExecuteAsync(
            connection,
            $"""
            IF SCHEMA_ID(N'{schema}') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'{schema}.FirstOutbox', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[FirstOutbox];
                IF OBJECT_ID(N'{schema}.SecondOutbox', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[SecondOutbox];
                DROP SCHEMA [{schema}];
            END;
            """);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> OutboxTableCountAsync(
        SqlConnection connection,
        string schema)
    {
        return await CountAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM sys.tables AS tables
            INNER JOIN sys.schemas AS schemas
                ON schemas.schema_id = tables.schema_id
            WHERE schemas.name = @schema
              AND tables.name IN (N'FirstOutbox', N'SecondOutbox');
            """,
            schema);
    }

    private static async Task<int> OutboxIndexCountAsync(
        SqlConnection connection,
        string schema)
    {
        return await CountAsync(
            connection,
            """
            SELECT COUNT(*)
            FROM sys.indexes AS indexes
            INNER JOIN sys.tables AS tables
                ON tables.object_id = indexes.object_id
            INNER JOIN sys.schemas AS schemas
                ON schemas.schema_id = tables.schema_id
            WHERE schemas.name = @schema
              AND tables.name IN (N'FirstOutbox', N'SecondOutbox')
              AND indexes.name IN
              (
                  N'IX_FirstOutbox_Pending',
                  N'IX_FirstOutbox_ExpiredProcessing',
                  N'IX_FirstOutbox_ClaimedBy',
                  N'IX_SecondOutbox_Pending',
                  N'IX_SecondOutbox_ExpiredProcessing',
                  N'IX_SecondOutbox_ClaimedBy'
              );
            """,
            schema);
    }

    private static async Task<int> ColumnCountAsync(
        SqlConnection connection,
        string schema,
        string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.columns AS columns
            INNER JOIN sys.tables AS tables
                ON tables.object_id = columns.object_id
            INNER JOIN sys.schemas AS schemas
                ON schemas.schema_id = tables.schema_id
            WHERE schemas.name = @schema
              AND tables.name = @table;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAsync(
        SqlConnection connection,
        string sql,
        string schema)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@schema", schema);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
