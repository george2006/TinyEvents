using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TinyEvents.PostgreSql.AdoNet;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class AdoNetPostgreSqlEndToEndRuntimeTests
{
    private readonly PostgreSqlFixture fixture;

    public AdoNetPostgreSqlEndToEndRuntimeTests(PostgreSqlFixture fixture)
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
        var session = scope.ServiceProvider.GetRequiredService<TestApplicationDbSession>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
        var userId = Guid.NewGuid();

        await session.ExecuteInTransactionAsync(async (_, _, cancellationToken) =>
        {
            await publisher.PublishAsync(new UserCreated(userId), cancellationToken);
        });
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

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddScoped(_ => new TestApplicationDbSession(fixture.ConnectionString));
        services.UsePostgreSqlAdoNetOutbox(options =>
        {
            options.UseCurrentTransaction(serviceProvider =>
            {
                var session = serviceProvider.GetRequiredService<TestApplicationDbSession>();

                return session.CurrentTransaction is null
                    ? null
                    : new TinyPostgreSqlAdoNetTransactionContext(
                        session.Connection,
                        session.CurrentTransaction);
            });
            options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
            {
                var connection = new NpgsqlConnection(fixture.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                return connection;
            });
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
        var session = scope.ServiceProvider.GetRequiredService<TestApplicationDbSession>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();

        await session.ExecuteInTransactionAsync(async (_, _, cancellationToken) =>
        {
            await publisher.PublishAsync(new UserCreated(userId), cancellationToken);
        });
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
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Status" FROM "TinyOutbox";""";
        var result = await command.ExecuteScalarAsync();
        return (TinyOutboxMessageStatus)Convert.ToInt32(result);
    }

    private sealed class TestApplicationDbSession
    {
        private readonly string connectionString;
        private DbConnection? connection;

        public TestApplicationDbSession(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public DbConnection Connection =>
            connection ?? throw new InvalidOperationException("The application session has no active connection.");

        public DbTransaction? CurrentTransaction { get; private set; }

        public async ValueTask ExecuteInTransactionAsync(
            Func<DbConnection, DbTransaction, CancellationToken, ValueTask> work,
            CancellationToken cancellationToken = default)
        {
            await using var openedConnection = new NpgsqlConnection(connectionString);
            await openedConnection.OpenAsync(cancellationToken);
            await using var transaction = await openedConnection.BeginTransactionAsync(cancellationToken);
            connection = openedConnection;
            CurrentTransaction = transaction;

            try
            {
                await work(connection, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
            finally
            {
                CurrentTransaction = null;
                connection = null;
            }
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
}
