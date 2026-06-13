using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TinyEvents.PostgreSql.AdoNet;
using Xunit;

namespace TinyEvents.PostgreSql.Tests;

public sealed class AdoNetPostgreSqlEndToEndRuntimeTests : IClassFixture<PostgreSqlFixture>
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
        services.AddSingleton<TinyEventTypeDescriptor>(
            new TinyEventTypeDescriptor(typeof(UserCreated).FullName!, typeof(UserCreated)));
        services.AddScoped<IEventConsumer<UserCreated>, RecordingConsumer>();

        return services.BuildServiceProvider();
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
