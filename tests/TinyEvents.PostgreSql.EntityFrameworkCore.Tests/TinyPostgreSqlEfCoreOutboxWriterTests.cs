using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.PostgreSql.EntityFrameworkCore.Tests;

public sealed class TinyPostgreSqlEfCoreOutboxWriterTests
{
    [Fact]
    public void Writer_rejects_null_db_context()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TinyPostgreSqlEfCoreOutboxWriter<TestDbContext>(null!));
    }

    [Fact]
    public async Task Writer_rejects_null_message()
    {
        await using var dbContext = NewTestDbContext();
        var writer = new TinyPostgreSqlEfCoreOutboxWriter<TestDbContext>(dbContext);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await writer.AddAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Writer_observes_cancellation_before_tracking_message()
    {
        await using var dbContext = NewTestDbContext();
        var writer = new TinyPostgreSqlEfCoreOutboxWriter<TestDbContext>(dbContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await writer.AddAsync(NewMessage(), cancellation.Token));

        Assert.Empty(dbContext.ChangeTracker.Entries<TinyOutboxMessage>());
    }

    [Fact]
    public async Task Writer_adds_outbox_message_without_saving_changes()
    {
        await using var dbContext = NewTestDbContext();
        var writer = new TinyPostgreSqlEfCoreOutboxWriter<TestDbContext>(dbContext);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Single(dbContext.ChangeTracker.Entries<TinyOutboxMessage>());
        Assert.Empty(await dbContext.Set<TinyOutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task Writer_message_is_persisted_by_caller_save_changes()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = NewOptions(databaseName);

        await using (var dbContext = new TestDbContext(options))
        {
            var writer = new TinyPostgreSqlEfCoreOutboxWriter<TestDbContext>(dbContext);

            await writer.AddAsync(NewMessage(), CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new TestDbContext(options))
        {
            Assert.Single(await dbContext.Set<TinyOutboxMessage>().ToListAsync());
        }
    }

    private static TestDbContext NewTestDbContext()
    {
        return new TestDbContext(NewOptions(Guid.NewGuid().ToString()));
    }

    private static DbContextOptions<TestDbContext> NewOptions(string databaseName)
    {
        return new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
    }

    private static TinyOutboxMessage NewMessage()
    {
        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "UserCreated",
            Payload = "{}",
            Status = TinyOutboxMessageStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
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
