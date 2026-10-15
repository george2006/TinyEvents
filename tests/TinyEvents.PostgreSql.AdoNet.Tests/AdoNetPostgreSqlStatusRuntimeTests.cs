using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class AdoNetPostgreSqlStatusRuntimeTests
{
    private static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture fixture;

    public AdoNetPostgreSqlStatusRuntimeTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Empty_outbox_has_zero_counts_and_no_pending_age_or_retained_success()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using var scope = services.CreateScope();

        var status = await scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>().ReadAsync();

        Assert.Equal(ObservedAtUtc, status.ObservedAtUtc);
        Assert.Equal(0, status.PendingCount);
        Assert.Equal(0, status.ProcessingCount);
        Assert.Equal(0, status.ExpiredProcessingCount);
        Assert.Equal(0, status.FailedCount);
        Assert.Null(status.OldestPendingAtUtc);
        Assert.Null(status.OldestPendingAge);
        Assert.Null(status.LastProcessedAtUtc);
    }

    [PostgreSqlIntegrationFact]
    public async Task Observation_counts_states_and_expired_leases_without_changing_messages()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using var scope = services.CreateScope();
        await SeedAsync(
            Message(TinyOutboxMessageStatus.Pending, -10),
            Message(TinyOutboxMessageStatus.Pending, -20, nextAttempt: ObservedAtUtc.AddHours(1)),
            Message(TinyOutboxMessageStatus.Processing, -30, leaseExpiry: ObservedAtUtc.AddMinutes(-1)),
            Message(TinyOutboxMessageStatus.Processing, -40, leaseExpiry: ObservedAtUtc),
            Message(TinyOutboxMessageStatus.Processing, -50, leaseExpiry: ObservedAtUtc.AddMinutes(1)),
            Message(TinyOutboxMessageStatus.Failed, -200),
            Message(TinyOutboxMessageStatus.Processed, -300, processedAt: ObservedAtUtc.AddMinutes(-5)),
            Message(TinyOutboxMessageStatus.Processed, -400, processedAt: ObservedAtUtc.AddMinutes(-2)));
        var before = await ReadMessagesJsonAsync();

        var status = await scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>().ReadAsync();

        Assert.Equal(ObservedAtUtc, status.ObservedAtUtc);
        Assert.Equal(2, status.PendingCount);
        Assert.Equal(3, status.ProcessingCount);
        Assert.Equal(2, status.ExpiredProcessingCount);
        Assert.Equal(1, status.FailedCount);
        Assert.Equal(ObservedAtUtc.AddMinutes(-50), status.OldestPendingAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(50), status.OldestPendingAge);
        Assert.Equal(ObservedAtUtc.AddMinutes(-2), status.LastProcessedAtUtc);
        Assert.Equal(before, await ReadMessagesJsonAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Future_retry_contributes_to_pending_age_before_it_is_due()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using var scope = services.CreateScope();
        await SeedAsync(
            Message(TinyOutboxMessageStatus.Pending, -60, nextAttempt: ObservedAtUtc.AddDays(1)));

        var status = await scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>().ReadAsync();

        Assert.Equal(1, status.PendingCount);
        Assert.Equal(TimeSpan.FromHours(1), status.OldestPendingAge);
    }

    [PostgreSqlIntegrationFact]
    public async Task Terminal_failure_has_no_pending_age_and_purged_success_is_no_longer_reported()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using var scope = services.CreateScope();
        await SeedAsync(
            Message(TinyOutboxMessageStatus.Failed, -200),
            Message(TinyOutboxMessageStatus.Processed, -100, processedAt: ObservedAtUtc.AddMinutes(-1)));
        var reader = scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();
        var beforeCleanup = await reader.ReadAsync();
        var cleanup = scope.ServiceProvider.GetRequiredService<ITinyOutboxCleanupStore>();

        await cleanup.DeleteProcessedBeforeAsync(ObservedAtUtc, 10, CancellationToken.None);
        var afterCleanup = await reader.ReadAsync();

        Assert.Equal(ObservedAtUtc.AddMinutes(-1), beforeCleanup.LastProcessedAtUtc);
        Assert.Null(afterCleanup.LastProcessedAtUtc);
        Assert.Null(afterCleanup.OldestPendingAge);
        Assert.Equal(0, afterCleanup.PendingCount);
        Assert.Equal(1, afterCleanup.FailedCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task Reader_uses_and_disposes_a_worker_connection_for_each_observation()
    {
        await fixture.ResetSchemaAsync();
        var connections = new List<NpgsqlConnection>();
        using var services = BuildServices(connectionOpened: connections.Add);
        using var scope = services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();

        await reader.ReadAsync();
        await reader.ReadAsync();

        Assert.Equal(2, connections.Count);
        Assert.NotSame(connections[0], connections[1]);
        foreach (var connection in connections)
        {
            Assert.Equal(ConnectionState.Closed, connection.State);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenAsync());
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task Reader_observes_only_the_configured_schema_and_table()
    {
        await fixture.ResetSchemaAsync();
        await SeedAsync(Message(TinyOutboxMessageStatus.Pending, -5));
        await ExecuteSqlAsync("""DROP SCHEMA IF EXISTS "StatusReaderTests" CASCADE;""");
        using var customServices = BuildServices("StatusReaderTests.Events");
        using var customScope = customServices.CreateScope();
        await customScope.ServiceProvider.GetRequiredService<ITinyEventsMigrator>().MigrateAsync(CancellationToken.None);
        var reader = customScope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();

        var emptyStatus = await reader.ReadAsync();
        await ExecuteSqlAsync("""
            INSERT INTO "StatusReaderTests"."Events" SELECT * FROM "public"."TinyOutbox";
            """);
        var populatedStatus = await reader.ReadAsync();

        Assert.Equal(0, emptyStatus.PendingCount);
        Assert.Equal(1, populatedStatus.PendingCount);
        Assert.Equal(ObservedAtUtc.AddMinutes(-5), populatedStatus.OldestPendingAtUtc);
    }

    [PostgreSqlIntegrationFact]
    public async Task Database_errors_are_not_converted_to_an_empty_observation()
    {
        await fixture.ResetSchemaAsync();
        var connections = new List<NpgsqlConnection>();
        using var services = BuildServices("MissingStatusOutbox", connections.Add);
        using var scope = services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();

        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await reader.ReadAsync());

        Assert.Equal(PostgresErrorCodes.UndefinedTable, exception.SqlState);
        var connection = Assert.Single(connections);
        Assert.Equal(ConnectionState.Closed, connection.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenAsync());
    }

    private ServiceProvider BuildServices(
        string tableName = "TinyOutbox",
        Action<NpgsqlConnection>? connectionOpened = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new ObservationClock());
        services.UsePostgreSqlAdoNetOutbox(options =>
        {
            options.TableName = tableName;
            options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
            {
                var connection = new NpgsqlConnection(fixture.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                connectionOpened?.Invoke(connection);
                return connection;
            });
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static TinyOutboxMessage Message(
        TinyOutboxMessageStatus status,
        int creationOffsetMinutes,
        DateTimeOffset? leaseExpiry = null,
        DateTimeOffset? nextAttempt = null,
        DateTimeOffset? processedAt = null)
    {
        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "StatusTestEvent",
            Payload = "{\"private\":true}",
            Status = status,
            CreatedAtUtc = ObservedAtUtc.AddMinutes(creationOffsetMinutes),
            ClaimExpiresAtUtc = leaseExpiry,
            NextAttemptAtUtc = nextAttempt,
            ProcessedAtUtc = processedAt
        };
    }

    private async Task SeedAsync(params TinyOutboxMessage[] messages)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var message in messages)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = TinyPostgreSqlAdoNetSql.Insert(TinyPostgreSqlAdoNetTableName.Parse("TinyOutbox"));
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@Id", message.Id);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@EventType", message.EventType);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@Payload", message.Payload);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@Status", (int)message.Status);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@AttemptCount", message.AttemptCount);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ClaimedBy", message.ClaimedBy);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ClaimedAtUtc", message.ClaimedAtUtc);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ClaimExpiresAtUtc", message.ClaimExpiresAtUtc);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@CreatedAtUtc", message.CreatedAtUtc);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@NextAttemptAtUtc", message.NextAttemptAtUtc);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ProcessedAtUtc", message.ProcessedAtUtc);
            TinyPostgreSqlAdoNetCommandParameters.Add(command, "@LastError", message.LastError);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<string> ReadMessagesJsonAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT jsonb_agg(to_jsonb(outbox) ORDER BY "Id")::text FROM "TinyOutbox" AS outbox;
            """;
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ObservationClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return ObservedAtUtc;
        }
    }
}
