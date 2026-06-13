using System.Data.Common;
using TinyEvents.Sample.PostgreSql.AdoNet.Contracts;
using TinyEvents.Sample.PostgreSql.AdoNet.Events;
using TinyEvents.Sample.PostgreSql.AdoNet.Infrastructure;

namespace TinyEvents.Sample.PostgreSql.AdoNet.UseCases;

public sealed class RegisterUserUseCase
{
    private readonly SampleAdoNetTransaction transaction;
    private readonly ITinyEventPublisher events;

    public RegisterUserUseCase(
        SampleAdoNetTransaction transaction,
        ITinyEventPublisher events)
    {
        this.transaction = transaction;
        this.events = events;
    }

    public async ValueTask<RegisterUserResult> RegisterAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var userId = Guid.NewGuid();

        try
        {
            await InsertUserAsync(
                transaction.Connection,
                transaction.Transaction,
                userId,
                email,
                cancellationToken);
            await events.PublishAsync(new UserCreated(userId, email), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new RegisterUserResult(userId, email);
    }

    private static async ValueTask InsertUserAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "Users" ("Id", "Email")
            VALUES (@Id, @Email);
            """;
        command.AddParameter("@Id", userId);
        command.AddParameter("@Email", email);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
