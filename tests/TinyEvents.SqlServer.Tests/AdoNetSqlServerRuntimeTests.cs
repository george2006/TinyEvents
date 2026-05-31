using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents.SqlServer.AdoNet;
using Xunit;

namespace TinyEvents.SqlServer.Tests;

public sealed class AdoNetSqlServerRuntimeTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public AdoNetSqlServerRuntimeTests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [SqlServerIntegrationFact]
    public async Task Application_transaction_commits_business_data_and_outbox_message_together()
    {
        await fixture.ResetSchemaAsync();
        var services = BuildServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ApplicationDbSession>();
        var events = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var userId = Guid.NewGuid();

        await session.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
        {
            await InsertUserAsync(connection, transaction, userId, "user@example.com", ct);
            await events.PublishAsync(new UserCreated(userId, "user@example.com"), ct);
        });

        Assert.Equal(1, await CountAsync("dbo.Users"));
        Assert.Equal(1, await CountAsync("dbo.TinyOutbox"));
    }

    [SqlServerIntegrationFact]
    public async Task Application_transaction_rolls_back_business_data_and_outbox_message_together()
    {
        await fixture.ResetSchemaAsync();
        var services = BuildServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ApplicationDbSession>();
        var events = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await InsertUserAsync(connection, transaction, userId, "user@example.com", ct);
                await events.PublishAsync(new UserCreated(userId, "user@example.com"), ct);
                throw new InvalidOperationException("rollback");
            }));

        Assert.Equal(0, await CountAsync("dbo.Users"));
        Assert.Equal(0, await CountAsync("dbo.TinyOutbox"));
    }

    [SqlServerIntegrationFact]
    public async Task Competing_workers_claim_message_only_once()
    {
        await fixture.ResetSchemaAsync();
        await InsertOutboxMessageAsync(Guid.NewGuid());
        var services = BuildServices();
        var now = DateTimeOffset.UtcNow;

        var first = ClaimInNewScopeAsync(services, "worker-1", now);
        var second = ClaimInNewScopeAsync(services, "worker-2", now);

        var results = await Task.WhenAll(first, second);
        var totalClaimed = results.Sum(result => result.Count);

        Assert.Equal(1, totalClaimed);
    }

    [SqlServerIntegrationFact]
    public async Task Store_reclaims_expired_processing_message()
    {
        await fixture.ResetSchemaAsync();
        await InsertOutboxMessageAsync(
            Guid.NewGuid(),
            TinyOutboxMessageStatus.Processing,
            workerId: "dead-worker",
            claimExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        var services = BuildServices();

        var claimed = await ClaimInNewScopeAsync(services, "worker-2", DateTimeOffset.UtcNow);

        var message = Assert.Single(claimed);
        Assert.Equal("worker-2", message.ClaimedBy);
        Assert.Equal(TinyOutboxMessageStatus.Processing, message.Status);
    }

    [SqlServerIntegrationFact]
    public async Task Store_does_not_claim_active_processing_message()
    {
        await fixture.ResetSchemaAsync();
        await InsertOutboxMessageAsync(
            Guid.NewGuid(),
            TinyOutboxMessageStatus.Processing,
            workerId: "worker-1",
            claimExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5));
        var services = BuildServices();

        var claimed = await ClaimInNewScopeAsync(services, "worker-2", DateTimeOffset.UtcNow);

        Assert.Empty(claimed);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddScoped(_ => new ApplicationDbSession(fixture.ConnectionString));
        services.UseSqlServerAdoNetOutbox(options =>
        {
            options.UseCurrentTransaction(provider =>
            {
                var session = provider.GetRequiredService<ApplicationDbSession>();
                return session.CurrentTransaction is null
                    ? null
                    : new TinyAdoNetTransactionContext(session.Connection, session.CurrentTransaction);
            });
            options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
            {
                var connection = new SqlConnection(fixture.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                return connection;
            });
        });

        return services.BuildServiceProvider();
    }

    private async Task<IReadOnlyList<TinyOutboxMessage>> ClaimInNewScopeAsync(
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

    private async Task InsertUserAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO dbo.Users (Id, Email) VALUES (@Id, @Email);";
        AddParameter(command, "@Id", userId);
        AddParameter(command, "@Email", email);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertOutboxMessageAsync(
        Guid messageId,
        TinyOutboxMessageStatus status = TinyOutboxMessageStatus.Pending,
        string? workerId = null,
        DateTimeOffset? claimExpiresAtUtc = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.TinyOutbox
            (
                Id,
                EventType,
                Payload,
                Status,
                AttemptCount,
                ClaimedBy,
                ClaimedAtUtc,
                ClaimExpiresAtUtc,
                CreatedAtUtc
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
                @CreatedAtUtc
            );
            """;
        AddParameter(command, "@Id", messageId);
        AddParameter(command, "@EventType", typeof(UserCreated).FullName!);
        AddParameter(command, "@Payload", "{}");
        AddParameter(command, "@Status", (int)status);
        AddParameter(command, "@AttemptCount", 0);
        AddParameter(command, "@ClaimedBy", workerId);
        AddParameter(command, "@ClaimedAtUtc", workerId is null ? null : DateTimeOffset.UtcNow);
        AddParameter(command, "@ClaimExpiresAtUtc", claimExpiresAtUtc);
        AddParameter(command, "@CreatedAtUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string tableName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record UserCreated(Guid UserId, string Email);

    private sealed class ApplicationDbSession
    {
        private readonly string connectionString;
        private System.Data.Common.DbConnection? connection;

        public ApplicationDbSession(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public System.Data.Common.DbConnection Connection =>
            connection ?? throw new InvalidOperationException("The application session has no active connection.");

        public System.Data.Common.DbTransaction? CurrentTransaction { get; private set; }

        public async ValueTask ExecuteInTransactionAsync(
            Func<System.Data.Common.DbConnection, System.Data.Common.DbTransaction, CancellationToken, ValueTask> work,
            CancellationToken cancellationToken = default)
        {
            await using var openedConnection = new SqlConnection(connectionString);
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
}
