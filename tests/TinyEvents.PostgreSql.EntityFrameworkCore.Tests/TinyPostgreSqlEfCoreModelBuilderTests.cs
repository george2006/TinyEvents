using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.PostgreSql.EntityFrameworkCore.Tests;

public sealed class TinyPostgreSqlEfCoreModelBuilderTests
{
    [Fact]
    public void Model_builder_extension_rejects_null_model_builder()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TinyEventsModelBuilderExtensions.UseTinyEventsOutbox(null!));
    }

    [Fact]
    public void Model_builder_extension_maps_outbox_message()
    {
        using var dbContext = NewTestDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.NotNull(entity.FindPrimaryKey());
    }

    [Fact]
    public void Model_builder_extension_uses_default_table_name()
    {
        using var dbContext = NewTestDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Equal("TinyOutbox", entity.GetTableName());
        Assert.Null(entity.GetSchema());
    }

    [Fact]
    public void Model_builder_extension_uses_custom_table_name()
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
    public void Model_builder_extension_adds_pending_claim_lookup_index()
    {
        using var dbContext = NewTestDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Contains(
            entity.GetIndexes(),
            index => HasProperties(index, nameof(TinyOutboxMessage.Status), nameof(TinyOutboxMessage.NextAttemptAtUtc), nameof(TinyOutboxMessage.CreatedAtUtc)));
    }

    [Fact]
    public void Model_builder_extension_adds_expired_processing_claim_lookup_index()
    {
        using var dbContext = NewTestDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Contains(
            entity.GetIndexes(),
            index => HasProperties(index, nameof(TinyOutboxMessage.Status), nameof(TinyOutboxMessage.ClaimExpiresAtUtc)));
    }

    [Fact]
    public void Model_builder_extension_adds_claim_owner_lookup_index()
    {
        using var dbContext = NewTestDbContext();

        var entity = dbContext.Model.FindEntityType(typeof(TinyOutboxMessage));

        Assert.NotNull(entity);
        Assert.Contains(
            entity.GetIndexes(),
            index => HasProperties(index, nameof(TinyOutboxMessage.ClaimedBy), nameof(TinyOutboxMessage.Status)));
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
}
