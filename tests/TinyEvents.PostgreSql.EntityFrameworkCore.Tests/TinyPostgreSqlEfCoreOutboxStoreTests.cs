using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.PostgreSql.EntityFrameworkCore.Tests;

public sealed class TinyPostgreSqlEfCoreOutboxStoreTests
{
    [Fact]
    public void Store_rejects_null_db_context()
    {
        var options = new TinyEventsPostgreSqlEntityFrameworkCoreOptions();

        Assert.Throws<ArgumentNullException>(
            () => new TinyPostgreSqlEfCoreOutboxStore<TestDbContext>(null!, options));
    }

    [Fact]
    public void Store_rejects_null_options()
    {
        using var dbContext = NewTestDbContext();

        Assert.Throws<ArgumentNullException>(
            () => new TinyPostgreSqlEfCoreOutboxStore<TestDbContext>(dbContext, null!));
    }

    [Fact]
    public async Task Claim_pending_rejects_null_worker_id()
    {
        using var dbContext = NewTestDbContext();
        var store = NewStore(dbContext);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.ClaimPendingAsync(1, null!, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task Mark_processed_rejects_null_worker_id()
    {
        using var dbContext = NewTestDbContext();
        var store = NewStore(dbContext);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.MarkProcessedAsync(Guid.NewGuid(), null!, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task Mark_failed_rejects_null_worker_id()
    {
        using var dbContext = NewTestDbContext();
        var store = NewStore(dbContext);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.MarkFailedAsync(Guid.NewGuid(), null!, "boom", 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task Mark_failed_rejects_null_error()
    {
        using var dbContext = NewTestDbContext();
        var store = NewStore(dbContext);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.MarkFailedAsync(Guid.NewGuid(), "worker", null!, 1, null, CancellationToken.None));
    }

    [Fact]
    public void Claim_sql_rejects_null_table_name()
    {
        Assert.Throws<ArgumentNullException>(() => TinyPostgreSqlEfCoreSql.ClaimPending(null!));
    }

    [Fact]
    public void Claim_sql_uses_atomic_update_with_postgre_sql_skip_locked()
    {
        var sql = TinyPostgreSqlEfCoreSql.ClaimPending(TinyPostgreSqlEfCoreTableName.Parse("public.TinyOutbox"));

        Assert.Contains("WITH claimed AS", sql);
        Assert.Contains("FOR UPDATE SKIP LOCKED", sql);
        Assert.Contains("UPDATE \"public\".\"TinyOutbox\" AS outbox", sql);
        Assert.Contains("FROM claimed", sql);
        Assert.Contains("RETURNING", sql);
        Assert.Contains("@WorkerId", sql);
        Assert.Contains("@ClaimExpiresAtUtc", sql);
    }

    [Fact]
    public void Claim_sql_reclaims_expired_processing_messages()
    {
        var sql = TinyPostgreSqlEfCoreSql.ClaimPending(TinyPostgreSqlEfCoreTableName.Parse("TinyOutbox"));

        Assert.Contains("\"Status\" = @ProcessingStatus", sql);
        Assert.Contains("\"ClaimExpiresAtUtc\" <= @Now", sql);
    }

    [Fact]
    public void Claim_sql_does_not_claim_future_retry_messages()
    {
        var sql = TinyPostgreSqlEfCoreSql.ClaimPending(TinyPostgreSqlEfCoreTableName.Parse("TinyOutbox"));

        Assert.Contains("\"NextAttemptAtUtc\" IS NULL OR \"NextAttemptAtUtc\" <= @Now", sql);
    }

    [Fact]
    public void Mark_processed_sql_rejects_null_table_name()
    {
        Assert.Throws<ArgumentNullException>(() => TinyPostgreSqlEfCoreSql.MarkProcessed(null!));
    }

    [Fact]
    public void Mark_processed_sql_limits_update_to_current_worker()
    {
        var sql = TinyPostgreSqlEfCoreSql.MarkProcessed(TinyPostgreSqlEfCoreTableName.Parse("TinyOutbox"));

        Assert.Contains("\"ClaimedBy\" = @WorkerId", sql);
        Assert.Contains("\"Status\" = @ProcessingStatus", sql);
        Assert.Contains("@ProcessedAtUtc", sql);
    }

    [Fact]
    public void Mark_failed_sql_rejects_null_table_name()
    {
        Assert.Throws<ArgumentNullException>(() => TinyPostgreSqlEfCoreSql.MarkFailed(null!));
    }

    [Fact]
    public void Mark_failed_sql_limits_update_to_current_worker()
    {
        var sql = TinyPostgreSqlEfCoreSql.MarkFailed(TinyPostgreSqlEfCoreTableName.Parse("TinyOutbox"));

        Assert.Contains("\"ClaimedBy\" = @WorkerId", sql);
        Assert.Contains("\"Status\" = @ProcessingStatus", sql);
        Assert.Contains("@NextAttemptAtUtc", sql);
        Assert.Contains("@LastError", sql);
    }

    private static TinyPostgreSqlEfCoreOutboxStore<TestDbContext> NewStore(TestDbContext dbContext)
    {
        return new TinyPostgreSqlEfCoreOutboxStore<TestDbContext>(
            dbContext,
            new TinyEventsPostgreSqlEntityFrameworkCoreOptions());
    }

    private static TestDbContext NewTestDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
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
}
