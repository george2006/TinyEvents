using Microsoft.Data.SqlClient;
using TinyEvents.Migrations;
using TinyEvents.Migrations.SqlServer;
using Xunit;

namespace TinyEvents.SqlServer.Tests;

public sealed class SqlServerMigrationHistoryTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public SqlServerMigrationHistoryTests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [SqlServerIntegrationFact]
    public async Task Bootstrap_creates_the_schema_and_history_table()
    {
        const string schema = "migration_bootstrap";
        const string outboxTable = "Events";
        await ResetAsync(schema, outboxTable);
        await using var connection = await OpenConnectionAsync();
        var history = History(schema, outboxTable);

        await history.EnsureSchemaAsync(connection, CancellationToken.None);

        Assert.True(await SchemaExistsAsync(connection, schema));
        Assert.False(await history.ExistsAsync(connection, CancellationToken.None));

        await history.EnsureExistsAsync(connection, CancellationToken.None);

        Assert.True(await history.ExistsAsync(connection, CancellationToken.None));
        Assert.Equal(
            ["Version", "Name", "Checksum", "AppliedAtUtc"],
            await ColumnNamesAsync(connection, schema, outboxTable + "Migrations"));
    }

    [SqlServerIntegrationFact]
    public async Task Append_and_read_preserve_order_and_the_exact_utc_timestamp()
    {
        const string schema = "migration_history";
        const string outboxTable = "Events";
        await ResetAsync(schema, outboxTable);
        await using var connection = await OpenConnectionAsync();
        var migration1AppliedAtUtc =
            new DateTimeOffset(2026, 7, 29, 10, 15, 30, TimeSpan.Zero);
        var migration2AppliedAtUtc =
            new DateTimeOffset(2026, 7, 29, 10, 16, 30, TimeSpan.Zero);
        var timeProvider = new SequenceTimeProvider(
            migration2AppliedAtUtc,
            migration1AppliedAtUtc);
        var history = History(schema, outboxTable, timeProvider);
        await history.EnsureSchemaAsync(connection, CancellationToken.None);
        await history.EnsureExistsAsync(connection, CancellationToken.None);
        var migration1 = Migration(1, "001_CreateTinyOutbox");
        var migration2 = Migration(2, "002_AddDeliveryPriority");

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await history.AppendAsync(connection, transaction, migration2, CancellationToken.None);
            await history.AppendAsync(connection, transaction, migration1, CancellationToken.None);
            await transaction.CommitAsync();
        }

        var appliedMigrations = await history.ReadAsync(connection, CancellationToken.None);

        Assert.Collection(
            appliedMigrations,
            migration =>
            {
                Assert.Equal(migration1.Version, migration.Version);
                Assert.Equal(migration1.Name, migration.Name);
                Assert.Equal(migration1.Checksum, migration.Checksum);
                Assert.Equal(migration1AppliedAtUtc, migration.AppliedAtUtc);
            },
            migration =>
            {
                Assert.Equal(migration2.Version, migration.Version);
                Assert.Equal(migration2.Name, migration.Name);
                Assert.Equal(migration2.Checksum, migration.Checksum);
                Assert.Equal(migration2AppliedAtUtc, migration.AppliedAtUtc);
            });
    }

    [SqlServerIntegrationFact]
    public async Task Append_participates_in_the_supplied_transaction()
    {
        const string schema = "migration_transaction";
        const string outboxTable = "Events";
        await ResetAsync(schema, outboxTable);
        await using var connection = await OpenConnectionAsync();
        var history = History(schema, outboxTable);
        await history.EnsureSchemaAsync(connection, CancellationToken.None);
        await history.EnsureExistsAsync(connection, CancellationToken.None);

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await history.AppendAsync(
                connection,
                transaction,
                Migration(1, "001_CreateTinyOutbox"),
                CancellationToken.None);
            await transaction.RollbackAsync();
        }

        Assert.Empty(await history.ReadAsync(connection, CancellationToken.None));
    }

    [SqlServerIntegrationFact]
    public async Task Outbox_existence_requires_a_user_table_in_the_exact_schema()
    {
        const string schema = "migration_objects";
        const string outboxTable = "Events";
        await ResetAsync(schema, outboxTable);
        await using var connection = await OpenConnectionAsync();
        var history = History(schema, outboxTable);
        await history.EnsureSchemaAsync(connection, CancellationToken.None);

        await ExecuteAsync(connection, $"CREATE VIEW [{schema}].[{outboxTable}] AS SELECT 1 AS [Value];");

        Assert.False(await history.OutboxTableExistsAsync(connection, CancellationToken.None));

        await ExecuteAsync(connection, $"DROP VIEW [{schema}].[{outboxTable}];");
        await ExecuteAsync(connection, $"CREATE TABLE [{schema}].[{outboxTable}] ([Id] int NOT NULL);");

        Assert.True(await history.OutboxTableExistsAsync(connection, CancellationToken.None));
    }

    [SqlServerIntegrationFact]
    public async Task Failed_history_bootstrap_does_not_leave_a_history_table()
    {
        const string schema = "migration_failure";
        const string outboxTable = "Events";
        await ResetAsync(schema, outboxTable);
        await using var connection = await OpenConnectionAsync();
        var history = History(schema, outboxTable);
        await history.EnsureSchemaAsync(connection, CancellationToken.None);
        await ExecuteAsync(
            connection,
            $"""
            CREATE TABLE [{schema}].[ConstraintOwner]
            (
                [Version] bigint NOT NULL,
                CONSTRAINT [PK_{outboxTable}Migrations] PRIMARY KEY ([Version])
            );
            """);

        await Assert.ThrowsAsync<SqlException>(
            () => history.EnsureExistsAsync(connection, CancellationToken.None));

        Assert.False(await history.ExistsAsync(connection, CancellationToken.None));
    }

    private static SqlServerMigrationHistory History(
        string schema,
        string outboxTable,
        TimeProvider? timeProvider = null)
    {
        var identity = SqlServerMigrationTableIdentity.Parse($"{schema}.{outboxTable}");
        return new SqlServerMigrationHistory(
            identity,
            timeProvider ?? TimeProvider.System);
    }

    private async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task ResetAsync(string schema, string outboxTable)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            $"""
            IF SCHEMA_ID(N'{schema}') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'[{schema}].[{outboxTable}Migrations]', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[{outboxTable}Migrations];
                IF OBJECT_ID(N'[{schema}].[{outboxTable}]', N'V') IS NOT NULL
                    DROP VIEW [{schema}].[{outboxTable}];
                IF OBJECT_ID(N'[{schema}].[{outboxTable}]', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[{outboxTable}];
                IF OBJECT_ID(N'[{schema}].[ConstraintOwner]', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[ConstraintOwner];
                DROP SCHEMA [{schema}];
            END;
            """);
    }

    private static TinyEventsMigration Migration(long version, string name)
    {
        return new TinyEventsMigration(version, name, $"SELECT {version};");
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> SchemaExistsAsync(
        SqlConnection connection,
        string schema)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN SCHEMA_ID(@schema) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@schema", schema);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<IReadOnlyList<string>> ColumnNamesAsync(
        SqlConnection connection,
        string schema,
        string table)
    {
        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT columns.name
            FROM sys.columns AS columns
            INNER JOIN sys.tables AS tables
                ON tables.object_id = columns.object_id
            INNER JOIN sys.schemas AS schemas
                ON schemas.schema_id = tables.schema_id
            WHERE schemas.name = @schema
              AND tables.name = @table
            ORDER BY columns.column_id;
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

    private sealed class SequenceTimeProvider : TimeProvider
    {
        private readonly Queue<DateTimeOffset> timestamps;

        internal SequenceTimeProvider(params DateTimeOffset[] timestamps)
        {
            this.timestamps = new Queue<DateTimeOffset>(timestamps);
        }

        public override DateTimeOffset GetUtcNow()
        {
            return timestamps.Dequeue();
        }
    }
}
