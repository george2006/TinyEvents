using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TinyEvents.Migrations.PostgreSql;
using TinyEvents.PostgreSql.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.PostgreSql.EntityFrameworkCore.Tests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class EfCorePostgreSqlStoreRuntimeTests
{
    private readonly PostgreSqlFixture fixture;

    public EfCorePostgreSqlStoreRuntimeTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Processor_publishes_consumes_and_marks_message_processed()
    {
        RecordingConsumer.Consumed.Clear();
        await fixture.ResetSchemaAsync();
        using var provider = BuildServices();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
        var userId = Guid.NewGuid();

        await publisher.PublishAsync(new UserCreated(userId));
        await dbContext.SaveChangesAsync();
        await processor.ProcessPendingAsync();

        var consumed = Assert.Single(RecordingConsumer.Consumed);
        Assert.Equal(userId, consumed.UserId);
        Assert.Equal(TinyOutboxMessageStatus.Processed, await ReadStatusAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Store_retries_failed_message_when_next_attempt_is_due()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        var now = DateTimeOffset.UtcNow;
        var nextAttemptAtUtc = now.AddMinutes(5);

        await PublishUserCreatedAsync(services, Guid.NewGuid());
        var message = Assert.Single(await ClaimInNewScopeAsync(services, "worker-1", now));

        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>();
            await store.MarkFailedAsync(
                message.Id,
                "worker-1",
                "retry later",
                1,
                nextAttemptAtUtc,
                CancellationToken.None);
        }

        Assert.Empty(await ClaimInNewScopeAsync(services, "worker-2", now));

        var retriedMessage = Assert.Single(await ClaimInNewScopeAsync(
            services,
            "worker-2",
            nextAttemptAtUtc));
        Assert.Equal("worker-2", retriedMessage.ClaimedBy);
        Assert.Equal(TinyOutboxMessageStatus.Processing, retriedMessage.Status);
        Assert.Equal(1, retriedMessage.AttemptCount);
        Assert.Equal("retry later", retriedMessage.LastError);
    }

    [PostgreSqlIntegrationFact]
    public async Task Store_does_not_reclaim_terminal_failure()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        var now = DateTimeOffset.UtcNow;

        await PublishUserCreatedAsync(services, Guid.NewGuid());
        var message = Assert.Single(await ClaimInNewScopeAsync(services, "worker-1", now));

        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>();
            await store.MarkFailedAsync(
                message.Id,
                "worker-1",
                "permanent failure",
                1,
                nextAttemptAtUtc: null,
                CancellationToken.None);
        }

        var claimedAfterLeaseExpired = await ClaimInNewScopeAsync(
            services,
            "worker-2",
            now.AddHours(1));

        Assert.Empty(claimedAfterLeaseExpired);
    }

    [PostgreSqlIntegrationFact]
    public async Task Store_claims_due_pending_message()
    {
        await fixture.ResetSchemaAsync();
        var messageId = Guid.NewGuid();
        await InsertOutboxMessageAsync(messageId);
        await using var dbContext = NewDbContext();
        var store = NewStore(dbContext);
        var now = DateTimeOffset.UtcNow;

        var claimed = await store.ClaimPendingAsync(1, "worker-1", now, TimeSpan.FromMinutes(5), CancellationToken.None);

        var message = Assert.Single(claimed);
        Assert.Equal(messageId, message.Id);
        Assert.Equal("worker-1", message.ClaimedBy);
        Assert.Equal(TinyOutboxMessageStatus.Processing, message.Status);
    }

    [PostgreSqlIntegrationFact]
    public async Task Store_does_not_claim_future_retry_message()
    {
        await fixture.ResetSchemaAsync();
        await InsertOutboxMessageAsync(
            Guid.NewGuid(),
            nextAttemptAtUtc: DateTimeOffset.UtcNow.AddMinutes(5));
        await using var dbContext = NewDbContext();
        var store = NewStore(dbContext);

        var claimed = await store.ClaimPendingAsync(1, "worker-1", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Empty(claimed);
    }

    [PostgreSqlIntegrationFact]
    public async Task Store_reclaims_expired_processing_message()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        var now = DateTimeOffset.UtcNow;

        await PublishUserCreatedAsync(services, Guid.NewGuid());
        Assert.Single(await ClaimInNewScopeAsync(services, "dead-worker", now));

        var claimed = await ClaimInNewScopeAsync(
            services,
            "worker-2",
            now.AddMinutes(5));

        var message = Assert.Single(claimed);
        Assert.Equal("worker-2", message.ClaimedBy);
        Assert.Equal(TinyOutboxMessageStatus.Processing, message.Status);
    }

    [PostgreSqlIntegrationFact]
    public async Task Store_does_not_claim_active_processing_message()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        var now = DateTimeOffset.UtcNow;

        await PublishUserCreatedAsync(services, Guid.NewGuid());
        Assert.Single(await ClaimInNewScopeAsync(services, "worker-1", now));

        var claimedBySecondWorker = await ClaimInNewScopeAsync(
            services,
            "worker-2",
            now.AddMinutes(1));

        Assert.Empty(claimedBySecondWorker);
    }

    [PostgreSqlIntegrationFact]
    public async Task Competing_workers_claim_message_only_once()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        var now = DateTimeOffset.UtcNow;

        await PublishUserCreatedAsync(services, Guid.NewGuid());

        var first = ClaimInNewScopeAsync(services, "worker-1", now);
        var second = ClaimInNewScopeAsync(services, "worker-2", now);

        var results = await Task.WhenAll(first, second);
        var totalClaimed = results.Sum(result => result.Count);

        Assert.Equal(1, totalClaimed);
    }

    [PostgreSqlIntegrationFact]
    public async Task Mark_processed_throws_when_message_is_owned_by_another_worker()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        var now = DateTimeOffset.UtcNow;

        await PublishUserCreatedAsync(services, Guid.NewGuid());
        var message = Assert.Single(await ClaimInNewScopeAsync(services, "worker-1", now));

        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            async () => await store.MarkProcessedAsync(
                message.Id,
                "worker-2",
                now,
                CancellationToken.None));

        Assert.Equal(
            TinyOutboxMessageStatus.Processing,
            await ReadStatusAsync(message.Id));

        await store.MarkProcessedAsync(
            message.Id,
            "worker-1",
            now,
            CancellationToken.None);

        Assert.Equal(
            TinyOutboxMessageStatus.Processed,
            await ReadStatusAsync(message.Id));
    }

    [PostgreSqlIntegrationFact]
    public async Task Mark_failed_with_retry_marks_pending_for_owned_message()
    {
        await fixture.ResetSchemaAsync();
        var messageId = Guid.NewGuid();
        var nextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);
        await InsertOutboxMessageAsync(messageId, TinyOutboxMessageStatus.Processing, workerId: "worker-1");
        await using var dbContext = NewDbContext();
        var store = NewStore(dbContext);

        await store.MarkFailedAsync(messageId, "worker-1", "boom", 3, nextAttemptAtUtc, CancellationToken.None);

        var row = await ReadFailureAsync(messageId);
        Assert.Equal(TinyOutboxMessageStatus.Pending, row.Status);
        Assert.Equal(3, row.AttemptCount);
        Assert.Equal("boom", row.LastError);
        Assert.NotNull(row.NextAttemptAtUtc);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString);
        });
        services.UsePostgreSqlEntityFrameworkCoreOutbox<TestDbContext>(options =>
        {
            options.TableName = "TinyOutbox";
        });
        services.AddSingleton<ITinyEventDispatcher>(
            new TinyEventDispatcher<UserCreated>(typeof(UserCreated).FullName!));
        services.AddScoped<IEventConsumer<UserCreated>, RecordingConsumer>();

        return services.BuildServiceProvider();
    }

    private static async Task PublishUserCreatedAsync(
        ServiceProvider services,
        Guid userId)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();

        await publisher.PublishAsync(new UserCreated(userId));
        await dbContext.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<TinyOutboxMessage>> ClaimInNewScopeAsync(
        ServiceProvider services,
        string workerId,
        DateTimeOffset now)
    {
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>();

        return await store.ClaimPendingAsync(
            maxCount: 1,
            workerId: workerId,
            now: now,
            claimTimeout: TimeSpan.FromMinutes(5),
            cancellationToken: CancellationToken.None);
    }

    private TestDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        return new TestDbContext(options);
    }

    private static TinyPostgreSqlEfCoreOutboxStore<TestDbContext> NewStore(
        TestDbContext dbContext)
    {
        return new TinyPostgreSqlEfCoreOutboxStore<TestDbContext>(
            dbContext,
            new TinyEventsPostgreSqlEntityFrameworkCoreOptions());
    }

    [PostgreSqlIntegrationFact]
    public async Task EF_registration_migrates_and_closes_DbContext_connection()
    {
        const string schema = "MigratorEfAdapter";
        await using (var resetConnection =
            new NpgsqlConnection(fixture.ConnectionString))
        {
            await resetConnection.OpenAsync();
            await using var resetCommand = resetConnection.CreateCommand();
            resetCommand.CommandText =
                $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;";
            await resetCommand.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(
            options => options.UseNpgsql(fixture.ConnectionString));
        services.UsePostgreSqlEntityFrameworkCoreOutbox<TestDbContext>(
            options => options.TableName = $"{schema}.Events");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var migrator = scope.ServiceProvider
            .GetRequiredService<PostgreSqlTinyEventsMigrator>();

        await migrator.MigrateAsync(CancellationToken.None);

        Assert.Equal(
            System.Data.ConnectionState.Closed,
            dbContext.Database.GetDbConnection().State);
        await using var verificationConnection =
            new NpgsqlConnection(fixture.ConnectionString);
        await verificationConnection.OpenAsync();
        await using var verificationCommand =
            verificationConnection.CreateCommand();
        verificationCommand.CommandText =
            $"SELECT COUNT(*) FROM \"{schema}\".\"EventsMigrations\";";
        Assert.Equal(
            1L,
            Assert.IsType<long>(
                await verificationCommand.ExecuteScalarAsync()));
    }

    private async Task InsertOutboxMessageAsync(
        Guid messageId,
        TinyOutboxMessageStatus status = TinyOutboxMessageStatus.Pending,
        string? workerId = null,
        DateTimeOffset? claimedAtUtc = null,
        DateTimeOffset? claimExpiresAtUtc = null,
        DateTimeOffset? nextAttemptAtUtc = null)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "TinyOutbox"
            (
                "Id",
                "EventType",
                "Payload",
                "Status",
                "AttemptCount",
                "ClaimedBy",
                "ClaimedAtUtc",
                "ClaimExpiresAtUtc",
                "CreatedAtUtc",
                "NextAttemptAtUtc"
            )
            VALUES
            (
                @Id,
                @EventType,
                @Payload,
                @Status,
                @AttemptCount,
                @ClaimedBy,
                @ClaimedAtUtc,
                @ClaimExpiresAtUtc,
                @CreatedAtUtc,
                @NextAttemptAtUtc
            );
            """;
        AddParameter(command, "@Id", messageId);
        AddParameter(command, "@EventType", typeof(UserCreated).FullName!);
        AddParameter(command, "@Payload", "{}");
        AddParameter(command, "@Status", (int)status);
        AddParameter(command, "@AttemptCount", 0);
        AddParameter(command, "@ClaimedBy", workerId);
        AddParameter(command, "@ClaimedAtUtc", claimedAtUtc);
        AddParameter(command, "@ClaimExpiresAtUtc", claimExpiresAtUtc);
        AddParameter(command, "@CreatedAtUtc", DateTimeOffset.UtcNow);
        AddParameter(command, "@NextAttemptAtUtc", nextAttemptAtUtc);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<TinyOutboxMessageStatus> ReadStatusAsync(Guid messageId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Status" FROM "TinyOutbox" WHERE "Id" = @Id;""";
        AddParameter(command, "@Id", messageId);
        var result = await command.ExecuteScalarAsync();
        return (TinyOutboxMessageStatus)Convert.ToInt32(result);
    }

    private async Task<TinyOutboxMessageStatus> ReadStatusAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Status" FROM "TinyOutbox";""";
        var result = await command.ExecuteScalarAsync();
        return (TinyOutboxMessageStatus)Convert.ToInt32(result);
    }

    private async Task<FailureRow> ReadFailureAsync(Guid messageId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Status", "AttemptCount", "LastError", "NextAttemptAtUtc"
            FROM "TinyOutbox"
            WHERE "Id" = @Id;
            """;
        AddParameter(command, "@Id", messageId);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new FailureRow(
            (TinyOutboxMessageStatus)reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    private static void AddParameter(
        NpgsqlCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseTinyEventsOutbox();
        }
    }

    private sealed record UserCreated(Guid UserId);

    private sealed class RecordingConsumer : IEventConsumer<UserCreated>
    {
        public static List<UserCreated> Consumed { get; } = new List<UserCreated>();

        public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
        {
            Consumed.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record FailureRow(
        TinyOutboxMessageStatus Status,
        int AttemptCount,
        string LastError,
        DateTimeOffset? NextAttemptAtUtc);
}
