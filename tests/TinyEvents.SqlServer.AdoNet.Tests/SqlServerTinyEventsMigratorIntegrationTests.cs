using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents.Migrations;
using TinyEvents.Migrations.SqlServer;
using TinyEvents.SqlServer.AdoNet;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class SqlServerTinyEventsMigratorIntegrationTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public SqlServerTinyEventsMigratorIntegrationTests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [SqlServerIntegrationFact]
    public async Task Fresh_migration_applies_once_and_a_second_run_is_current()
    {
        const string schema = "migrator_fresh";
        var appliedAtUtc = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);
        await ResetSchemaAsync(schema);
        var migrator = Migrator(schema, new FixedTimeProvider(appliedAtUtc));

        await migrator.MigrateAsync(CancellationToken.None);
        await migrator.MigrateAsync(CancellationToken.None);

        var appliedMigrations = await ReadHistoryAsync(schema);
        var migration = Assert.Single(appliedMigrations);
        Assert.Equal(1, migration.Version);
        Assert.Equal("001_CreateTinyOutbox", migration.Name);
        Assert.Equal(appliedAtUtc, migration.AppliedAtUtc);
        Assert.True(await TableExistsAsync(schema, "Events"));
    }

    [SqlServerIntegrationFact]
    public async Task Existing_alpha_outbox_is_baselined_without_executing_migration_SQL()
    {
        const string schema = "migrator_alpha";
        await ResetSchemaAsync(schema);
        await CreateSchemaAsync(schema);
        await ExecuteAsync(
            $"""
            CREATE TABLE [{schema}].[Events]
            (
                [Id] uniqueidentifier NOT NULL
            );
            """);
        var migrator = Migrator(schema, new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        await migrator.MigrateAsync(CancellationToken.None);

        var appliedMigration = Assert.Single(await ReadHistoryAsync(schema));
        var expectedMigration = SqlServerMigration001CreateOutbox.Create(
            SqlServerMigrationTableIdentity.Parse($"{schema}.Events"));
        Assert.Equal(expectedMigration.Checksum, appliedMigration.Checksum);
        Assert.Equal(0, await SecondaryIndexCountAsync(schema, "Events"));
    }

    [SqlServerIntegrationFact]
    public async Task Existing_empty_history_recovers_through_normal_migration_planning()
    {
        const string schema = "migrator_interrupted";
        await ResetSchemaAsync(schema);
        var identity = SqlServerMigrationTableIdentity.Parse($"{schema}.Events");
        var history = new SqlServerMigrationHistory(identity, TimeProvider.System);
        await using (var connection = await OpenConnectionAsync())
        {
            await history.EnsureSchemaAsync(connection, CancellationToken.None);
            await history.EnsureExistsAsync(connection, CancellationToken.None);
        }
        await CreateCompatibleOutboxWithoutIndexesAsync(schema);

        var migrator = Migrator(schema, new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        await migrator.MigrateAsync(CancellationToken.None);

        Assert.Single(await ReadHistoryAsync(schema));
        Assert.Equal(3, await SecondaryIndexCountAsync(schema, "Events"));
    }

    [SqlServerIntegrationFact]
    public async Task Failed_later_migration_preserves_earlier_commit_and_rolls_back_its_SQL()
    {
        const string schema = "migrator_atomic";
        await ResetSchemaAsync(schema);
        var identity = SqlServerMigrationTableIdentity.Parse($"{schema}.Events");
        var migration1 = SqlServerMigration001CreateOutbox.Create(identity);
        var migration2 = new TinyEventsMigration(
            2,
            "002_" + new string('A', 510),
            $"CREATE TABLE [{schema}].[AtomicStep] ([Id] int NOT NULL);");
        var catalog = new TinyEventsMigrationCatalog([migration1, migration2]);
        var migrator = Migrator(
            identity,
            catalog,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<SqlException>(
            () => migrator.MigrateAsync(CancellationToken.None));

        var appliedMigrations = await ReadHistoryAsync(schema);
        Assert.Equal([1L], appliedMigrations.Select(migration => migration.Version));
        Assert.False(await TableExistsAsync(schema, "AtomicStep"));
    }

    [SqlServerIntegrationFact]
    public async Task Retry_resumes_after_the_last_committed_migration()
    {
        const string schema = "migrator_resume";
        await ResetSchemaAsync(schema);
        var identity = SqlServerMigrationTableIdentity.Parse($"{schema}.Events");
        var migration1 = SqlServerMigration001CreateOutbox.Create(identity);
        var failingMigration2 = new TinyEventsMigration(
            2,
            "002_AddSecondStep",
            "THIS IS NOT VALID SQL;");
        var failingCatalog = new TinyEventsMigrationCatalog([migration1, failingMigration2]);

        await Assert.ThrowsAsync<SqlException>(
            () => Migrator(
                identity,
                failingCatalog,
                new FixedTimeProvider(DateTimeOffset.UnixEpoch))
                .MigrateAsync(CancellationToken.None));

        var successfulMigration2 = new TinyEventsMigration(
            2,
            "002_AddSecondStep",
            $"CREATE TABLE [{schema}].[SecondStep] ([Id] int NOT NULL);");
        var successfulCatalog = new TinyEventsMigrationCatalog(
            [migration1, successfulMigration2]);

        await Migrator(
            identity,
            successfulCatalog,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch))
            .MigrateAsync(CancellationToken.None);

        var appliedMigrations = await ReadHistoryAsync(schema);
        Assert.Equal([1L, 2L], appliedMigrations.Select(migration => migration.Version));
        Assert.True(await TableExistsAsync(schema, "SecondStep"));
    }

    [SqlServerIntegrationFact]
    public async Task Concurrent_migrators_serialize_and_observe_committed_history()
    {
        const string schema = "migrator_concurrent";
        await ResetSchemaAsync(schema);
        var firstMigrator = Migrator(
            schema,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var secondMigrator = Migrator(
            schema,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        await Task.WhenAll(
            firstMigrator.MigrateAsync(CancellationToken.None),
            secondMigrator.MigrateAsync(CancellationToken.None));

        Assert.Single(await ReadHistoryAsync(schema));
        Assert.True(await TableExistsAsync(schema, "Events"));
    }

    [SqlServerIntegrationFact]
    public async Task ADO_NET_registration_reuses_the_worker_factory_and_disposes_its_connection()
    {
        const string schema = "migrator_ado_adapter";
        await ResetSchemaAsync(schema);
        var factoryCalls = 0;
        SqlConnection? migrationConnection = null;
        var services = new ServiceCollection();
        services.UseSqlServerAdoNetOutbox(options =>
        {
            options.TableName = $"{schema}.Events";
            options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
            {
                factoryCalls++;
                migrationConnection = new SqlConnection(fixture.ConnectionString);
                await migrationConnection.OpenAsync(cancellationToken);
                return migrationConnection;
            });
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var migrator =
            scope.ServiceProvider.GetRequiredService<SqlServerTinyEventsMigrator>();

        await migrator.MigrateAsync(CancellationToken.None);

        Assert.Equal(1, factoryCalls);
        Assert.NotNull(migrationConnection);
        Assert.Equal(System.Data.ConnectionState.Closed, migrationConnection.State);
        Assert.Single(await ReadHistoryAsync(schema));
    }

    private SqlServerTinyEventsMigrator Migrator(
        string schema,
        TimeProvider timeProvider)
    {
        return new SqlServerTinyEventsMigrator(
            new TestMigrationConnectionFactory(fixture.ConnectionString),
            $"{schema}.Events",
            timeProvider);
    }

    private SqlServerTinyEventsMigrator Migrator(
        SqlServerMigrationTableIdentity identity,
        TinyEventsMigrationCatalog catalog,
        TimeProvider timeProvider)
    {
        return new SqlServerTinyEventsMigrator(
            new TestMigrationConnectionFactory(fixture.ConnectionString),
            identity,
            timeProvider,
            catalog);
    }

    private async Task<IReadOnlyList<AppliedTinyEventsMigration>> ReadHistoryAsync(
        string schema)
    {
        var identity = SqlServerMigrationTableIdentity.Parse($"{schema}.Events");
        var history = new SqlServerMigrationHistory(identity, TimeProvider.System);
        await using var connection = await OpenConnectionAsync();

        return await history.ReadAsync(connection, CancellationToken.None);
    }

    private async Task ResetSchemaAsync(string schema)
    {
        await ExecuteAsync(
            $"""
            IF SCHEMA_ID(N'{schema}') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'{schema}.EventsMigrations', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[EventsMigrations];
                IF OBJECT_ID(N'{schema}.AtomicStep', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[AtomicStep];
                IF OBJECT_ID(N'{schema}.SecondStep', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[SecondStep];
                IF OBJECT_ID(N'{schema}.Events', N'U') IS NOT NULL
                    DROP TABLE [{schema}].[Events];
                DROP SCHEMA [{schema}];
            END;
            """);
    }

    private async Task CreateSchemaAsync(string schema)
    {
        await ExecuteAsync($"CREATE SCHEMA [{schema}];");
    }

    private async Task CreateCompatibleOutboxWithoutIndexesAsync(string schema)
    {
        await ExecuteAsync(
            $"""
            CREATE TABLE [{schema}].[Events]
            (
                [Id] uniqueidentifier NOT NULL
                    CONSTRAINT [PK_Events] PRIMARY KEY,
                [EventType] nvarchar(512) NOT NULL,
                [Payload] nvarchar(max) NOT NULL,
                [Status] int NOT NULL,
                [AttemptCount] int NOT NULL,
                [ClaimedBy] nvarchar(256) NULL,
                [ClaimedAtUtc] datetimeoffset NULL,
                [ClaimExpiresAtUtc] datetimeoffset NULL,
                [CreatedAtUtc] datetimeoffset NOT NULL,
                [NextAttemptAtUtc] datetimeoffset NULL,
                [ProcessedAtUtc] datetimeoffset NULL,
                [LastError] nvarchar(max) NULL
            );
            """);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> TableExistsAsync(string schema, string table)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM sys.tables AS tables
                INNER JOIN sys.schemas AS schemas
                    ON schemas.schema_id = tables.schema_id
                WHERE schemas.name = @schema
                  AND tables.name = @table
            )
            THEN 1
            ELSE 0
            END;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private async Task<int> SecondaryIndexCountAsync(string schema, string table)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.indexes AS indexes
            INNER JOIN sys.tables AS tables
                ON tables.object_id = indexes.object_id
            INNER JOIN sys.schemas AS schemas
                ON schemas.schema_id = tables.schema_id
            WHERE schemas.name = @schema
              AND tables.name = @table
              AND indexes.index_id > 0
              AND indexes.is_primary_key = 0;
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed class TestMigrationConnectionFactory
        : ISqlServerMigrationConnectionFactory
    {
        private readonly string connectionString;

        internal TestMigrationConnectionFactory(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public async ValueTask<SqlServerMigrationConnection> CreateOpenConnectionAsync(
            CancellationToken cancellationToken)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return new SqlServerMigrationConnection(
                connection,
                ownsConnection: true,
                closeWhenDisposed: false);
        }
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
