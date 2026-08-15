using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TinyEvents.Migrations;
using TinyEvents.Migrations.PostgreSql;
using TinyEvents.PostgreSql.AdoNet;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class PostgreSqlTinyEventsMigratorIntegrationTests
    : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture fixture;

    public PostgreSqlTinyEventsMigratorIntegrationTests(
        PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Fresh_migration_applies_once_and_a_second_run_is_current()
    {
        const string schema = "MigratorFresh";
        var appliedAtUtc =
            new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);
        await ResetSchemaAsync(schema);
        var migrator = Migrator(
            schema,
            new FixedTimeProvider(appliedAtUtc));

        await migrator.MigrateAsync(CancellationToken.None);
        await migrator.MigrateAsync(CancellationToken.None);

        var applied = Assert.Single(await ReadHistoryAsync(schema));
        Assert.Equal(1, applied.Version);
        Assert.Equal("001_CreateTinyOutbox", applied.Name);
        Assert.Equal(appliedAtUtc, applied.AppliedAtUtc);
        Assert.True(await TableExistsAsync(schema, "Events"));
    }

    [PostgreSqlIntegrationFact]
    public async Task Existing_alpha_outbox_is_baselined_without_running_SQL()
    {
        const string schema = "MigratorAlpha";
        await ResetSchemaAsync(schema);
        await ExecuteAsync($"CREATE SCHEMA \"{schema}\";");
        await ExecuteAsync(
            $"CREATE TABLE \"{schema}\".\"Events\" (\"Id\" uuid NOT NULL);");
        var migrator =
            Migrator(schema, new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        await migrator.MigrateAsync(CancellationToken.None);

        var applied = Assert.Single(await ReadHistoryAsync(schema));
        var expected = PostgreSqlMigration001CreateOutbox.Create(
            PostgreSqlMigrationTableIdentity.Parse($"{schema}.Events"));
        Assert.Equal(expected.Checksum, applied.Checksum);
        Assert.Equal(0, await SecondaryIndexCountAsync(schema, "Events"));
    }

    [PostgreSqlIntegrationFact]
    public async Task Existing_empty_history_uses_normal_migration_planning()
    {
        const string schema = "MigratorInterrupted";
        await ResetSchemaAsync(schema);
        var identity =
            PostgreSqlMigrationTableIdentity.Parse($"{schema}.Events");
        var history =
            new PostgreSqlMigrationHistory(identity, TimeProvider.System);
        await using (var connection = await OpenConnectionAsync())
        {
            await history.EnsureSchemaAsync(connection, CancellationToken.None);
            await history.EnsureExistsAsync(connection, CancellationToken.None);
        }
        await CreateCompatibleOutboxWithoutIndexesAsync(schema);

        await Migrator(
                schema,
                new FixedTimeProvider(DateTimeOffset.UnixEpoch))
            .MigrateAsync(CancellationToken.None);

        Assert.Single(await ReadHistoryAsync(schema));
        Assert.Equal(3, await SecondaryIndexCountAsync(schema, "Events"));
    }

    [PostgreSqlIntegrationFact]
    public async Task Failed_later_migration_preserves_prior_commit_and_rolls_back()
    {
        const string schema = "MigratorAtomic";
        await ResetSchemaAsync(schema);
        var identity =
            PostgreSqlMigrationTableIdentity.Parse($"{schema}.Events");
        var migration1 = PostgreSqlMigration001CreateOutbox.Create(identity);
        var migration2 = new TinyEventsMigration(
            2,
            "002_AddAtomicStep",
            $"""
            CREATE TABLE "{schema}"."AtomicStep" ("Id" integer NOT NULL);
            SELECT 1 / 0;
            """);
        var catalog = new TinyEventsMigrationCatalog([migration1, migration2]);

        await Assert.ThrowsAsync<PostgresException>(
            () => Migrator(
                    identity,
                    catalog,
                    new FixedTimeProvider(DateTimeOffset.UnixEpoch))
                .MigrateAsync(CancellationToken.None));

        Assert.Equal(
            [1L],
            (await ReadHistoryAsync(schema))
                .Select(migration => migration.Version));
        Assert.False(await TableExistsAsync(schema, "AtomicStep"));
    }

    [PostgreSqlIntegrationFact]
    public async Task Retry_resumes_after_the_last_committed_migration()
    {
        const string schema = "MigratorResume";
        await ResetSchemaAsync(schema);
        var identity =
            PostgreSqlMigrationTableIdentity.Parse($"{schema}.Events");
        var migration1 = PostgreSqlMigration001CreateOutbox.Create(identity);
        var failingMigration2 = new TinyEventsMigration(
            2,
            "002_AddSecondStep",
            "THIS IS NOT VALID SQL;");

        await Assert.ThrowsAsync<PostgresException>(
            () => Migrator(
                    identity,
                    new TinyEventsMigrationCatalog(
                        [migration1, failingMigration2]),
                    new FixedTimeProvider(DateTimeOffset.UnixEpoch))
                .MigrateAsync(CancellationToken.None));

        var successfulMigration2 = new TinyEventsMigration(
            2,
            "002_AddSecondStep",
            $"CREATE TABLE \"{schema}\".\"SecondStep\" (\"Id\" integer);");
        await Migrator(
                identity,
                new TinyEventsMigrationCatalog(
                    [migration1, successfulMigration2]),
                new FixedTimeProvider(DateTimeOffset.UnixEpoch))
            .MigrateAsync(CancellationToken.None);

        Assert.Equal(
            [1L, 2L],
            (await ReadHistoryAsync(schema))
                .Select(migration => migration.Version));
        Assert.True(await TableExistsAsync(schema, "SecondStep"));
    }

    [PostgreSqlIntegrationFact]
    public async Task Concurrent_migrators_serialize_and_observe_history()
    {
        const string schema = "MigratorConcurrent";
        await ResetSchemaAsync(schema);
        var first =
            Migrator(schema, new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var second =
            Migrator(schema, new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        await Task.WhenAll(
            first.MigrateAsync(CancellationToken.None),
            second.MigrateAsync(CancellationToken.None));

        Assert.Single(await ReadHistoryAsync(schema));
        Assert.True(await TableExistsAsync(schema, "Events"));
    }

    [PostgreSqlIntegrationFact]
    public async Task ADO_NET_registration_reuses_factory_and_disposes_connection()
    {
        const string schema = "MigratorAdoAdapter";
        await ResetSchemaAsync(schema);
        var factoryCalls = 0;
        NpgsqlConnection? migrationConnection = null;
        var services = new ServiceCollection();
        services.UsePostgreSqlAdoNetOutbox(options =>
        {
            options.TableName = $"{schema}.Events";
            options.UseWorkerConnectionFactory(
                async (_, cancellationToken) =>
                {
                    factoryCalls++;
                    migrationConnection =
                        new NpgsqlConnection(fixture.ConnectionString);
                    await migrationConnection.OpenAsync(cancellationToken);
                    return migrationConnection;
                });
        });
        using var provider = services.BuildServiceProvider();

        await provider.MigrateTinyEventsAsync();

        Assert.Equal(1, factoryCalls);
        Assert.NotNull(migrationConnection);
        Assert.Equal(
            System.Data.ConnectionState.Closed,
            migrationConnection.State);
        Assert.Single(await ReadHistoryAsync(schema));
    }

    private PostgreSqlTinyEventsMigrator Migrator(
        string schema,
        TimeProvider timeProvider)
    {
        return new PostgreSqlTinyEventsMigrator(
            new TestMigrationConnectionFactory(fixture.ConnectionString),
            $"{schema}.Events",
            timeProvider);
    }

    private PostgreSqlTinyEventsMigrator Migrator(
        PostgreSqlMigrationTableIdentity identity,
        TinyEventsMigrationCatalog catalog,
        TimeProvider timeProvider)
    {
        return new PostgreSqlTinyEventsMigrator(
            new TestMigrationConnectionFactory(fixture.ConnectionString),
            identity,
            timeProvider,
            catalog);
    }

    private async Task<IReadOnlyList<AppliedTinyEventsMigration>>
        ReadHistoryAsync(string schema)
    {
        var identity =
            PostgreSqlMigrationTableIdentity.Parse($"{schema}.Events");
        var history =
            new PostgreSqlMigrationHistory(identity, TimeProvider.System);
        await using var connection = await OpenConnectionAsync();
        return await history.ReadAsync(connection, CancellationToken.None);
    }

    private Task ResetSchemaAsync(string schema)
    {
        return ExecuteAsync($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
    }

    private async Task CreateCompatibleOutboxWithoutIndexesAsync(string schema)
    {
        await ExecuteAsync(
            $"""
            CREATE TABLE "{schema}"."Events"
            (
                "Id" uuid NOT NULL CONSTRAINT "PK_Events" PRIMARY KEY,
                "EventType" text NOT NULL,
                "Payload" text NOT NULL,
                "Status" integer NOT NULL,
                "AttemptCount" integer NOT NULL,
                "ClaimedBy" text NULL,
                "ClaimedAtUtc" timestamp with time zone NULL,
                "ClaimExpiresAtUtc" timestamp with time zone NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "NextAttemptAtUtc" timestamp with time zone NULL,
                "ProcessedAtUtc" timestamp with time zone NULL,
                "LastError" text NULL
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
            SELECT EXISTS
            (
                SELECT 1
                FROM pg_catalog.pg_class AS tables
                INNER JOIN pg_catalog.pg_namespace AS schemas
                    ON schemas.oid = tables.relnamespace
                WHERE schemas.nspname = @schema
                  AND tables.relname = @table
                  AND tables.relkind IN ('r', 'p')
            );
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private async Task<int> SecondaryIndexCountAsync(
        string schema,
        string table)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM pg_catalog.pg_indexes
            WHERE schemaname = @schema
              AND tablename = @table
              AND indexname IN
              (
                  @pending,
                  @expired,
                  @claimedBy
              );
            """;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@pending", $"IX_{table}_Pending");
        command.Parameters.AddWithValue(
            "@expired",
            $"IX_{table}_ExpiredProcessing");
        command.Parameters.AddWithValue("@claimedBy", $"IX_{table}_ClaimedBy");
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed class TestMigrationConnectionFactory
        : IPostgreSqlMigrationConnectionFactory
    {
        private readonly string connectionString;

        internal TestMigrationConnectionFactory(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public async ValueTask<PostgreSqlMigrationConnection>
            CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return new PostgreSqlMigrationConnection(
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
