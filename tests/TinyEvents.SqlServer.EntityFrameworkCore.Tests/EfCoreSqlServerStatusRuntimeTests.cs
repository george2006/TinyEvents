using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TinyEvents.SqlServer.EntityFrameworkCore.Tests;

public sealed class EfCoreSqlServerStatusRuntimeTests : IClassFixture<SqlServerFixture>
{
    private static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly SqlServerFixture fixture;

    public EfCoreSqlServerStatusRuntimeTests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [SqlServerIntegrationFact]
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

    [SqlServerIntegrationFact]
    public async Task Observation_counts_states_and_expired_leases_without_changing_messages()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StatusDbContext>();
        await SeedAsync(dbContext,
            Message(TinyOutboxMessageStatus.Pending, -10),
            Message(TinyOutboxMessageStatus.Pending, -20, nextAttempt: ObservedAtUtc.AddHours(1)),
            Message(TinyOutboxMessageStatus.Processing, -30, leaseExpiry: ObservedAtUtc.AddMinutes(-1)),
            Message(TinyOutboxMessageStatus.Processing, -40, leaseExpiry: ObservedAtUtc),
            Message(TinyOutboxMessageStatus.Processing, -50, leaseExpiry: ObservedAtUtc.AddMinutes(1)),
            Message(TinyOutboxMessageStatus.Failed, -200),
            Message(TinyOutboxMessageStatus.Processed, -300, processedAt: ObservedAtUtc.AddMinutes(-5)),
            Message(TinyOutboxMessageStatus.Processed, -400, processedAt: ObservedAtUtc.AddMinutes(-2)));
        var before = await ReadMessagesJsonAsync(dbContext);

        var status = await scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>().ReadAsync();

        Assert.Equal(ObservedAtUtc, status.ObservedAtUtc);
        Assert.Equal(2, status.PendingCount);
        Assert.Equal(3, status.ProcessingCount);
        Assert.Equal(2, status.ExpiredProcessingCount);
        Assert.Equal(1, status.FailedCount);
        Assert.Equal(ObservedAtUtc.AddMinutes(-50), status.OldestPendingAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(50), status.OldestPendingAge);
        Assert.Equal(ObservedAtUtc.AddMinutes(-2), status.LastProcessedAtUtc);
        Assert.Equal(before, await ReadMessagesJsonAsync(dbContext));
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [SqlServerIntegrationFact]
    public async Task Future_retry_contributes_to_pending_age_before_it_is_due()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using var scope = services.CreateScope();
        await SeedAsync(scope.ServiceProvider.GetRequiredService<StatusDbContext>(),
            Message(TinyOutboxMessageStatus.Pending, -60, nextAttempt: ObservedAtUtc.AddDays(1)));

        var status = await scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>().ReadAsync();

        Assert.Equal(1, status.PendingCount);
        Assert.Equal(TimeSpan.FromHours(1), status.OldestPendingAge);
    }

    [SqlServerIntegrationFact]
    public async Task Terminal_failure_has_no_pending_age_and_purged_success_is_no_longer_reported()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using var scope = services.CreateScope();
        await SeedAsync(scope.ServiceProvider.GetRequiredService<StatusDbContext>(),
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

    [SqlServerIntegrationFact]
    public async Task Reader_uses_the_current_transaction_and_leaves_the_context_connection_open()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices();
        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<StatusDbContext>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            await SeedAsync(dbContext, Message(TinyOutboxMessageStatus.Pending, -5));

            var status = await scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>().ReadAsync();

            Assert.Equal(1, status.PendingCount);
            Assert.Same(transaction, dbContext.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, dbContext.Database.GetDbConnection().State);
            await transaction.RollbackAsync();
        }

        using var verificationScope = services.CreateScope();
        var persistedStatus = await verificationScope.ServiceProvider
            .GetRequiredService<ITinyOutboxStatusReader>().ReadAsync();
        Assert.Equal(0, persistedStatus.PendingCount);
    }

    [SqlServerIntegrationFact]
    public async Task Reader_observes_only_the_configured_schema_and_table()
    {
        await fixture.ResetSchemaAsync();
        using var defaultServices = BuildServices();
        using var defaultScope = defaultServices.CreateScope();
        var dbContext = defaultScope.ServiceProvider.GetRequiredService<StatusDbContext>();
        await SeedAsync(dbContext, Message(TinyOutboxMessageStatus.Pending, -5));
        await dbContext.Database.ExecuteSqlRawAsync("""
            DROP TABLE IF EXISTS [StatusReaderTests].[EventsMigrations];
            DROP TABLE IF EXISTS [StatusReaderTests].[Events];
            """);
        using var customServices = BuildServices("StatusReaderTests.Events");
        using var customScope = customServices.CreateScope();
        await customScope.ServiceProvider.GetRequiredService<ITinyEventsMigrator>().MigrateAsync(CancellationToken.None);
        var reader = customScope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();

        var emptyStatus = await reader.ReadAsync();
        await dbContext.Database.ExecuteSqlRawAsync("""
            INSERT INTO [StatusReaderTests].[Events] SELECT * FROM [dbo].[TinyOutbox];
            """);
        var populatedStatus = await reader.ReadAsync();

        Assert.Equal(0, emptyStatus.PendingCount);
        Assert.Equal(1, populatedStatus.PendingCount);
        Assert.Equal(ObservedAtUtc.AddMinutes(-5), populatedStatus.OldestPendingAtUtc);
    }

    [SqlServerIntegrationFact]
    public async Task Database_errors_are_not_converted_to_an_empty_observation()
    {
        await fixture.ResetSchemaAsync();
        using var services = BuildServices("MissingStatusOutbox");
        using var scope = services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();

        var exception = await Assert.ThrowsAsync<SqlException>(async () => await reader.ReadAsync());

        Assert.Equal(208, exception.Number);
    }

    private ServiceProvider BuildServices(string tableName = "TinyOutbox")
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new ObservationClock());
        services.AddDbContext<StatusDbContext>(options => options.UseSqlServer(fixture.ConnectionString));
        services.UseSqlServerEntityFrameworkCoreOutbox<StatusDbContext>(options => options.TableName = tableName);
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

    private static async Task SeedAsync(StatusDbContext dbContext, params TinyOutboxMessage[] messages)
    {
        dbContext.Set<TinyOutboxMessage>().AddRange(messages);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static async Task<string> ReadMessagesJsonAsync(StatusDbContext dbContext)
    {
        var messages = await dbContext.Set<TinyOutboxMessage>().AsNoTracking()
            .OrderBy(message => message.Id).ToListAsync();
        return JsonSerializer.Serialize(messages);
    }

    private sealed class ObservationClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return ObservedAtUtc;
        }
    }

    private sealed class StatusDbContext : DbContext
    {
        public StatusDbContext(DbContextOptions<StatusDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseTinyEventsOutbox();
        }
    }
}
