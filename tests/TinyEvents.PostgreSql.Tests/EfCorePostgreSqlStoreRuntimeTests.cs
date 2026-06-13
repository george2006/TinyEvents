using Microsoft.EntityFrameworkCore;
using Npgsql;
using TinyEvents.PostgreSql.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.PostgreSql.Tests;

public sealed class EfCorePostgreSqlStoreRuntimeTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture fixture;

    public EfCorePostgreSqlStoreRuntimeTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
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
        var messageId = Guid.NewGuid();
        await InsertOutboxMessageAsync(
            messageId,
            TinyOutboxMessageStatus.Processing,
            workerId: "dead-worker",
            claimedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            claimExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        await using var dbContext = NewDbContext();
        var store = NewStore(dbContext);

        var claimed = await store.ClaimPendingAsync(1, "worker-2", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);

        var message = Assert.Single(claimed);
        Assert.Equal(messageId, message.Id);
        Assert.Equal("worker-2", message.ClaimedBy);
        Assert.Equal(TinyOutboxMessageStatus.Processing, message.Status);
    }

    [PostgreSqlIntegrationFact]
    public async Task Store_does_not_claim_active_processing_message()
    {
        await fixture.ResetSchemaAsync();
        await InsertOutboxMessageAsync(
            Guid.NewGuid(),
            TinyOutboxMessageStatus.Processing,
            workerId: "worker-1",
            claimedAtUtc: DateTimeOffset.UtcNow,
            claimExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5));
        await using var dbContext = NewDbContext();
        var store = NewStore(dbContext);

        var claimed = await store.ClaimPendingAsync(1, "worker-2", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Empty(claimed);
    }

    [PostgreSqlIntegrationFact]
    public async Task Mark_processed_updates_only_message_owned_by_worker()
    {
        await fixture.ResetSchemaAsync();
        var ownedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await InsertOutboxMessageAsync(ownedId, TinyOutboxMessageStatus.Processing, workerId: "worker-1");
        await InsertOutboxMessageAsync(otherId, TinyOutboxMessageStatus.Processing, workerId: "worker-2");
        await using var dbContext = NewDbContext();
        var store = NewStore(dbContext);

        await store.MarkProcessedAsync(ownedId, "worker-1", DateTimeOffset.UtcNow, CancellationToken.None);
        await store.MarkProcessedAsync(otherId, "worker-1", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(TinyOutboxMessageStatus.Processed, await ReadStatusAsync(ownedId));
        Assert.Equal(TinyOutboxMessageStatus.Processing, await ReadStatusAsync(otherId));
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

    private TestDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        return new TestDbContext(options);
    }

    private static TinyPostgreSqlEfCoreOutboxStore<TestDbContext> NewStore(TestDbContext dbContext)
    {
        return new TinyPostgreSqlEfCoreOutboxStore<TestDbContext>(
            dbContext,
            new TinyEventsPostgreSqlEntityFrameworkCoreOptions());
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

    private sealed record FailureRow(
        TinyOutboxMessageStatus Status,
        int AttemptCount,
        string LastError,
        DateTimeOffset? NextAttemptAtUtc);
}
