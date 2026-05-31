using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents.SqlServer.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.SqlServer.Tests;

public sealed class EfCoreSqlServerRuntimeTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public EfCoreSqlServerRuntimeTests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [SqlServerIntegrationFact]
    public async Task Writer_commits_business_data_and_outbox_message_with_save_changes()
    {
        await fixture.ResetSchemaAsync();
        var services = BuildServices();
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var userId = Guid.NewGuid();

        dbContext.Users.Add(new UserRow
        {
            Id = userId,
            Email = "user@example.com"
        });

        await events.PublishAsync(new UserCreated(userId, "user@example.com"));
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, await dbContext.Users.CountAsync());
        Assert.Equal(1, await dbContext.Set<TinyOutboxMessage>().CountAsync());
    }

    [SqlServerIntegrationFact]
    public async Task Store_claims_message_using_sql_server_claim_sql()
    {
        await fixture.ResetSchemaAsync();
        var services = BuildServices();

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.Set<TinyOutboxMessage>().Add(NewMessage());
            await dbContext.SaveChangesAsync();
        }

        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>();

            var claimed = await store.ClaimPendingAsync(
                maxCount: 1,
                workerId: "ef-worker",
                now: DateTimeOffset.UtcNow,
                claimTimeout: TimeSpan.FromMinutes(5),
                cancellationToken: CancellationToken.None);

            var message = Assert.Single(claimed);
            Assert.Equal("ef-worker", message.ClaimedBy);
            Assert.Equal(TinyOutboxMessageStatus.Processing, message.Status);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Store_reclaims_expired_processing_message()
    {
        await fixture.ResetSchemaAsync();
        var services = BuildServices();

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.Set<TinyOutboxMessage>().Add(NewProcessingMessage(
                workerId: "dead-worker",
                claimExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1)));
            await dbContext.SaveChangesAsync();
        }

        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>();

            var claimed = await store.ClaimPendingAsync(
                maxCount: 1,
                workerId: "ef-worker-2",
                now: DateTimeOffset.UtcNow,
                claimTimeout: TimeSpan.FromMinutes(5),
                cancellationToken: CancellationToken.None);

            var message = Assert.Single(claimed);
            Assert.Equal("ef-worker-2", message.ClaimedBy);
        }
    }

    [SqlServerIntegrationFact]
    public async Task Store_does_not_claim_active_processing_message()
    {
        await fixture.ResetSchemaAsync();
        var services = BuildServices();

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.Set<TinyOutboxMessage>().Add(NewProcessingMessage(
                workerId: "ef-worker-1",
                claimExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5)));
            await dbContext.SaveChangesAsync();
        }

        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>();

            var claimed = await store.ClaimPendingAsync(
                maxCount: 1,
                workerId: "ef-worker-2",
                now: DateTimeOffset.UtcNow,
                claimTimeout: TimeSpan.FromMinutes(5),
                cancellationToken: CancellationToken.None);

            Assert.Empty(claimed);
        }
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlServer(fixture.ConnectionString);
        });
        services.UseSqlServerEntityFrameworkCoreOutbox<TestDbContext>();

        return services.BuildServiceProvider();
    }

    private static TinyOutboxMessage NewMessage()
    {
        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(UserCreated).FullName!,
            Payload = "{}",
            Status = TinyOutboxMessageStatus.Pending,
            AttemptCount = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static TinyOutboxMessage NewProcessingMessage(
        string workerId,
        DateTimeOffset claimExpiresAtUtc)
    {
        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(UserCreated).FullName!,
            Payload = "{}",
            Status = TinyOutboxMessageStatus.Processing,
            AttemptCount = 0,
            ClaimedBy = workerId,
            ClaimedAtUtc = DateTimeOffset.UtcNow,
            ClaimExpiresAtUtc = claimExpiresAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
            : base(options)
        {
        }

        public DbSet<UserRow> Users => Set<UserRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRow>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(user => user.Id);
                entity.Property(user => user.Email).IsRequired();
            });

            modelBuilder.UseTinyEventsOutbox();
        }
    }

    private sealed class UserRow
    {
        public Guid Id { get; set; }

        public string Email { get; set; } = string.Empty;
    }

    private sealed record UserCreated(Guid UserId, string Email);
}
