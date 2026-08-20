using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents.Migrations.SqlServer;
using TinyEvents.SqlServer.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.SqlServer.EntityFrameworkCore.Tests;

public sealed class EfCoreSqlServerRuntimeTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public EfCoreSqlServerRuntimeTests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [SqlServerIntegrationFact]
    public async Task Processor_publishes_consumes_and_marks_message_processed()
    {
        RecordingConsumer.Consumed.Clear();
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
        var userId = Guid.NewGuid();

        await publisher.PublishAsync(new UserCreated(userId, "user@example.com"));
        await dbContext.SaveChangesAsync();
        await processor.ProcessPendingAsync();

        var consumed = Assert.Single(RecordingConsumer.Consumed);
        Assert.Equal(userId, consumed.UserId);
        Assert.Equal(TinyOutboxMessageStatus.Processed, await ReadStatusAsync());
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

    [SqlServerIntegrationFact]
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

    [SqlServerIntegrationFact]
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

    [SqlServerIntegrationFact]
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

    [SqlServerIntegrationFact]
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

    [SqlServerIntegrationFact]
    public async Task Store_rejects_completion_from_worker_that_does_not_own_lease()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        var now = DateTimeOffset.UtcNow;

        await PublishUserCreatedAsync(services, Guid.NewGuid());
        var message = Assert.Single(await ClaimInNewScopeAsync(services, "worker-1", now));

        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () =>
            await store.MarkProcessedAsync(
                message.Id,
                "worker-2",
                now,
                CancellationToken.None));

        await store.MarkProcessedAsync(
            message.Id,
            "worker-1",
            now,
            CancellationToken.None);
    }

    [SqlServerIntegrationFact]
    public async Task EF_registration_migrates_and_closes_the_scoped_DbContext_connection()
    {
        const string schema = "migrator_ef_adapter";
        await using (var resetConnection = new SqlConnection(fixture.ConnectionString))
        {
            await resetConnection.OpenAsync();
            await using var resetCommand = resetConnection.CreateCommand();
            resetCommand.CommandText = $"""
                IF SCHEMA_ID(N'{schema}') IS NOT NULL
                BEGIN
                    IF OBJECT_ID(N'{schema}.EventsMigrations', N'U') IS NOT NULL
                        DROP TABLE [{schema}].[EventsMigrations];
                    IF OBJECT_ID(N'{schema}.Events', N'U') IS NOT NULL
                        DROP TABLE [{schema}].[Events];
                    DROP SCHEMA [{schema}];
                END;
                """;
            await resetCommand.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(
            options => options.UseSqlServer(fixture.ConnectionString));
        services.UseSqlServerEntityFrameworkCoreOutbox<TestDbContext>(
            options => options.TableName = $"{schema}.Events");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var migrator =
            scope.ServiceProvider.GetRequiredService<SqlServerTinyEventsMigrator>();

        await migrator.MigrateAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.Closed, dbContext.Database.GetDbConnection().State);
        await using var verificationConnection = new SqlConnection(fixture.ConnectionString);
        await verificationConnection.OpenAsync();
        await using var verificationCommand = verificationConnection.CreateCommand();
        verificationCommand.CommandText =
            $"SELECT COUNT(*) FROM [{schema}].[EventsMigrations];";
        Assert.Equal(1, Convert.ToInt32(await verificationCommand.ExecuteScalarAsync()));
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlServer(fixture.ConnectionString);
        });
        services.UseSqlServerEntityFrameworkCoreOutbox<TestDbContext>();
        services.AddSingleton<ITinyEventDispatcher>(new TinyEventDispatcher<UserCreated>());
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

        await publisher.PublishAsync(new UserCreated(userId, "user@example.com"));
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

    private async Task<TinyOutboxMessageStatus> ReadStatusAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM dbo.TinyOutbox;";
        var result = await command.ExecuteScalarAsync();
        return (TinyOutboxMessageStatus)Convert.ToInt32(result);
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

    private sealed class RecordingConsumer : IEventConsumer<UserCreated>
    {
        public static List<UserCreated> Consumed { get; } = new List<UserCreated>();

        public ValueTask ConsumeAsync(
            UserCreated @event,
            CancellationToken cancellationToken)
        {
            Consumed.Add(@event);
            return ValueTask.CompletedTask;
        }
    }
}
