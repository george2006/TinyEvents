using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents.SqlServer.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.SqlServer.EntityFrameworkCore.Tests;

public sealed class TinySqlServerEfCoreProviderTests
{
    [Fact]
    public void Model_builder_extension_maps_outbox_message()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new TestDbContext(options);

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.NotNull(entity.FindPrimaryKey());
    }

    [Fact]
    public void EF_model_builder_uses_default_table_name()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new TestDbContext(options);

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Equal("TinyOutbox", entity.GetTableName());
        Assert.Null(entity.GetSchema());
    }

    [Fact]
    public void EF_model_builder_uses_custom_table_name()
    {
        var options = new DbContextOptionsBuilder<CustomOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new CustomOutboxDbContext(options);

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Equal("MyOutbox", entity.GetTableName());
        Assert.Equal("app", entity.GetSchema());
    }

    [Fact]
    public void EF_model_builder_adds_pending_claim_lookup_index()
    {
        using var dbContext = NewTestDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Contains(
            entity.GetIndexes(),
            index => HasProperties(index, nameof(TinyOutboxMessage.Status), nameof(TinyOutboxMessage.NextAttemptAtUtc), nameof(TinyOutboxMessage.CreatedAtUtc)));
    }

    [Fact]
    public void EF_model_builder_adds_expired_processing_claim_lookup_index()
    {
        using var dbContext = NewTestDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Contains(
            entity.GetIndexes(),
            index => HasProperties(index, nameof(TinyOutboxMessage.Status), nameof(TinyOutboxMessage.ClaimExpiresAtUtc)));
    }

    [Fact]
    public void EF_model_builder_adds_claim_owner_lookup_index()
    {
        using var dbContext = NewTestDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Contains(
            entity.GetIndexes(),
            index => HasProperties(index, nameof(TinyOutboxMessage.ClaimedBy), nameof(TinyOutboxMessage.Status)));
    }

    [Fact]
    public void Use_sql_server_entity_framework_core_outbox_registers_writer_and_store()
    {
        var services = new ServiceCollection();

        services.UseSqlServerEntityFrameworkCoreOutbox<TestDbContext>();

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ITinyOutboxWriter)
                && descriptor.ImplementationType == typeof(TinySqlServerEfCoreOutboxWriter<TestDbContext>));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ITinyOutboxStore)
                && descriptor.ImplementationType == typeof(TinySqlServerEfCoreOutboxStore<TestDbContext>));
    }

    [Fact]
    public void EF_store_does_not_implement_writer()
    {
        Assert.False(typeof(ITinyOutboxWriter).IsAssignableFrom(typeof(TinySqlServerEfCoreOutboxStore<TestDbContext>)));
    }

    [Fact]
    public async Task Writer_adds_outbox_message_without_saving_changes()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new TestDbContext(options);
        var writer = new TinySqlServerEfCoreOutboxWriter<TestDbContext>(dbContext);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Single(dbContext.ChangeTracker.Entries<TinyOutboxMessage>());
        Assert.Empty(await dbContext.Set<TinyOutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task Writer_message_is_persisted_by_caller_save_changes()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var dbContext = new TestDbContext(options))
        {
            var writer = new TinySqlServerEfCoreOutboxWriter<TestDbContext>(dbContext);

            await writer.AddAsync(NewMessage(), CancellationToken.None);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = new TestDbContext(options))
        {
            Assert.Single(await dbContext.Set<TinyOutboxMessage>().ToListAsync());
        }
    }

    [Fact]
    public void Claim_sql_uses_atomic_update_with_sql_server_locking_hints()
    {
        var sql = TinySqlServerEfCoreSql.ClaimPending(TinySqlServerEfCoreTableName.Parse("dbo.TinyOutbox"));

        Assert.Contains("WITH (UPDLOCK, READPAST, ROWLOCK)", sql);
        Assert.Contains("UPDATE cte", sql);
        Assert.Contains("OUTPUT", sql);
        Assert.Contains("@WorkerId", sql);
    }

    [Fact]
    public void Claim_sql_reclaims_expired_processing_messages()
    {
        var sql = TinySqlServerEfCoreSql.ClaimPending(TinySqlServerEfCoreTableName.Parse("TinyOutbox"));

        Assert.Contains("Status = @ProcessingStatus", sql);
        Assert.Contains("ClaimExpiresAtUtc <= @Now", sql);
    }

    [Fact]
    public void Claim_sql_does_not_claim_future_retry_messages()
    {
        var sql = TinySqlServerEfCoreSql.ClaimPending(TinySqlServerEfCoreTableName.Parse("TinyOutbox"));

        Assert.Contains("NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now", sql);
    }

    [Fact]
    public void EF_provider_uses_configured_table_name_for_claiming()
    {
        var sql = TinySqlServerEfCoreSql.ClaimPending(TinySqlServerEfCoreTableName.Parse("app.MyOutbox"));

        Assert.Contains("FROM [app].[MyOutbox]", sql);
    }

    [Fact]
    public void EF_provider_uses_configured_table_name_for_processed_failed_updates()
    {
        var tableName = TinySqlServerEfCoreTableName.Parse("app.MyOutbox");

        var processed = TinySqlServerEfCoreSql.MarkProcessed(tableName);
        var failed = TinySqlServerEfCoreSql.MarkFailed(tableName);

        Assert.Contains("UPDATE [app].[MyOutbox]", processed);
        Assert.Contains("UPDATE [app].[MyOutbox]", failed);
    }

    [Fact]
    public void Mark_processed_sql_limits_update_to_current_worker()
    {
        var sql = TinySqlServerEfCoreSql.MarkProcessed(TinySqlServerEfCoreTableName.Parse("TinyOutbox"));

        Assert.Contains("ClaimedBy = @WorkerId", sql);
        Assert.Contains("Status = @ProcessingStatus", sql);
    }

    [Fact]
    public void Mark_failed_sql_limits_update_to_current_worker()
    {
        var sql = TinySqlServerEfCoreSql.MarkFailed(TinySqlServerEfCoreTableName.Parse("TinyOutbox"));

        Assert.Contains("ClaimedBy = @WorkerId", sql);
        Assert.Contains("Status = @ProcessingStatus", sql);
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

    private sealed class CustomOutboxDbContext : DbContext
    {
        public CustomOutboxDbContext(DbContextOptions<CustomOutboxDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseTinyEventsOutbox("app.MyOutbox");
        }
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

    private static TestDbContext NewTestDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    private static bool HasProperties(
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyIndex index,
        params string[] propertyNames)
    {
        return index.Properties
            .Select(property => property.Name)
            .SequenceEqual(propertyNames);
    }
}
