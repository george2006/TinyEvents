using Npgsql;
using TinyEvents.PostgreSql.AdoNet;
using Xunit;

namespace TinyEvents.PostgreSql.Tests;

public sealed class AdoNetPostgreSqlSchemaRuntimeTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture fixture;

    public AdoNetPostgreSqlSchemaRuntimeTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Schema_helper_creates_outbox_table_and_indexes()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var createCommand = connection.CreateCommand();
        createCommand.CommandText = TinyPostgreSqlAdoNetSchema.CreateOutboxSql();
        await createCommand.ExecuteNonQueryAsync();

        var tableExists = await ReadScalarAsync<long>(
            connection,
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
                AND table_name = 'TinyOutbox';
            """);

        var indexCount = await ReadScalarAsync<long>(
            connection,
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'public'
                AND tablename = 'TinyOutbox'
                AND indexname IN
                (
                    'IX_TinyOutbox_Pending',
                    'IX_TinyOutbox_ExpiredProcessing',
                    'IX_TinyOutbox_ClaimedBy'
                );
            """);

        Assert.Equal(1, tableExists);
        Assert.Equal(3, indexCount);
    }

    private static async Task<T> ReadScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<T>(value);
    }
}
