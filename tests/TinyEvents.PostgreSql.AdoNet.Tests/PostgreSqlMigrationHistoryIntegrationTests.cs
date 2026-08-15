using Npgsql;
using TinyEvents.Migrations;
using TinyEvents.Migrations.PostgreSql;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class PostgreSqlMigrationHistoryIntegrationTests
{
    private readonly PostgreSqlFixture fixture;

    public PostgreSqlMigrationHistoryIntegrationTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Bootstrap_creates_the_exact_schema_and_history_table()
    {
        const string schema = "MigrationBootstrap";
        const string table = "Events";
        await ResetAsync(schema);
        await using var connection = await OpenConnectionAsync();
        var history = History(schema, table);

        await history.EnsureSchemaAsync(connection, CancellationToken.None);
        Assert.False(await history.ExistsAsync(connection, CancellationToken.None));

        await history.EnsureExistsAsync(connection, CancellationToken.None);

        Assert.True(await history.ExistsAsync(connection, CancellationToken.None));
        Assert.Equal(
            ["Version", "Name", "Checksum", "AppliedAtUtc"],
            await ColumnNamesAsync(connection, schema, table + "Migrations"));
    }

    [PostgreSqlIntegrationFact]
    public async Task Append_and_read_preserve_order_timestamp_and_transaction()
    {
        const string schema = "MigrationHistory";
        const string table = "Events";
        await ResetAsync(schema);
        await using var connection = await OpenConnectionAsync();
        var timestamp = new DateTimeOffset(2026, 7, 30, 10, 15, 30, TimeSpan.Zero);
        var history = History(schema, table, new FixedTimeProvider(timestamp));
        await history.EnsureSchemaAsync(connection, CancellationToken.None);
        await history.EnsureExistsAsync(connection, CancellationToken.None);
        var migration = new TinyEventsMigration(1, "001_CreateTinyOutbox", "SELECT 1;");

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await history.AppendAsync(
                connection,
                transaction,
                migration,
                CancellationToken.None);
            await transaction.CommitAsync();
        }

        var applied = Assert.Single(
            await history.ReadAsync(connection, CancellationToken.None));
        Assert.Equal(migration.Version, applied.Version);
        Assert.Equal(migration.Name, applied.Name);
        Assert.Equal(migration.Checksum, applied.Checksum);
        Assert.Equal(timestamp, applied.AppliedAtUtc);

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await history.AppendAsync(
                connection,
                transaction,
                new TinyEventsMigration(2, "002_AddPriority", "SELECT 2;"),
                CancellationToken.None);
            await transaction.RollbackAsync();
        }

        Assert.Single(await history.ReadAsync(connection, CancellationToken.None));
    }

    [PostgreSqlIntegrationFact]
    public async Task Outbox_existence_accepts_tables_and_partitions_but_not_views()
    {
        const string schema = "MigrationObjects";
        const string table = "Events";
        await ResetAsync(schema);
        await using var connection = await OpenConnectionAsync();
        var history = History(schema, table);
        await history.EnsureSchemaAsync(connection, CancellationToken.None);

        await ExecuteAsync(connection, $"CREATE VIEW \"{schema}\".\"{table}\" AS SELECT 1;");
        Assert.False(await history.OutboxTableExistsAsync(connection, CancellationToken.None));
        await ExecuteAsync(connection, $"DROP VIEW \"{schema}\".\"{table}\";");
        await ExecuteAsync(
            connection,
            $"CREATE TABLE \"{schema}\".\"{table}\" (\"Id\" integer) PARTITION BY RANGE (\"Id\");");

        Assert.True(await history.OutboxTableExistsAsync(connection, CancellationToken.None));
    }

    private PostgreSqlMigrationHistory History(
        string schema,
        string table,
        TimeProvider? timeProvider = null)
    {
        return new PostgreSqlMigrationHistory(
            PostgreSqlMigrationTableIdentity.Parse($"{schema}.{table}"),
            timeProvider ?? TimeProvider.System);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task ResetAsync(string schema)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ColumnNamesAsync(
        NpgsqlConnection connection,
        string schema,
        string table)
    {
        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = @schema
              AND table_name = @table
            ORDER BY ordinal_position;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset timestamp;

        internal FixedTimeProvider(DateTimeOffset timestamp)
        {
            this.timestamp = timestamp;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return timestamp;
        }
    }
}
