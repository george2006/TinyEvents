using System.Data.Common;
using Npgsql;
using TinyEvents.PostgreSql.AdoNet;
using Xunit;

namespace TinyEvents.PostgreSql.Tests;

public sealed class AdoNetPostgreSqlWriterRuntimeTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture fixture;

    public AdoNetPostgreSqlWriterRuntimeTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Writer_commits_business_data_and_outbox_message_together()
    {
        await fixture.ResetSchemaAsync();
        var session = new ApplicationDbSession(fixture.ConnectionString);
        var writer = NewWriter(session);
        var userId = Guid.NewGuid();

        await session.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
        {
            await InsertUserAsync(connection, transaction, userId, "user@example.com", ct);
            await writer.AddAsync(NewMessage(userId), ct);
        });

        Assert.Equal(1, await CountAsync("\"Users\""));
        Assert.Equal(1, await CountAsync("\"TinyOutbox\""));
    }

    [PostgreSqlIntegrationFact]
    public async Task Writer_rolls_back_business_data_and_outbox_message_together()
    {
        await fixture.ResetSchemaAsync();
        var session = new ApplicationDbSession(fixture.ConnectionString);
        var writer = NewWriter(session);
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await InsertUserAsync(connection, transaction, userId, "user@example.com", ct);
                await writer.AddAsync(NewMessage(userId), ct);
                throw new InvalidOperationException("rollback");
            }));

        Assert.Equal(0, await CountAsync("\"Users\""));
        Assert.Equal(0, await CountAsync("\"TinyOutbox\""));
    }

    private TinyPostgreSqlAdoNetOutboxWriter NewWriter(ApplicationDbSession session)
    {
        var options = new TinyEventsPostgreSqlAdoNetOptions();
        options.UseCurrentTransaction(_ =>
        {
            return session.CurrentTransaction is null
                ? null
                : new TinyPostgreSqlAdoNetTransactionContext(
                    session.Connection,
                    session.CurrentTransaction);
        });

        return new TinyPostgreSqlAdoNetOutboxWriter(options, new EmptyServiceProvider());
    }

    private static TinyOutboxMessage NewMessage(Guid userId)
    {
        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(UserCreated).FullName!,
            Payload = $$"""{"userId":"{{userId}}"}""",
            Status = TinyOutboxMessageStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task InsertUserAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """INSERT INTO "Users" ("Id", "Email") VALUES (@Id, @Email);""";
        AddParameter(command, "@Id", userId);
        AddParameter(command, "@Email", email);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> CountAsync(string tableName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record UserCreated(Guid UserId);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class ApplicationDbSession
    {
        private readonly string connectionString;
        private DbConnection? connection;

        public ApplicationDbSession(string connectionString)
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
}
