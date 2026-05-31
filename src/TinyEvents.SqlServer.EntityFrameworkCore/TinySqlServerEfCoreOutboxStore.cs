using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TinyEvents.SqlServer.EntityFrameworkCore;

public sealed class TinySqlServerEfCoreOutboxStore<TDbContext> : ITinyOutboxStore
    where TDbContext : DbContext
{
    private readonly TDbContext dbContext;
    private readonly TinySqlServerEfCoreTableName tableName;

    public TinySqlServerEfCoreOutboxStore(
        TDbContext dbContext,
        TinyEventsSqlServerEntityFrameworkCoreOptions options)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        this.dbContext = dbContext;
        tableName = TinySqlServerEfCoreTableName.Parse(options.TableName);
    }

    public async ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
        int maxCount,
        string workerId,
        DateTimeOffset now,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken)
    {
        if (workerId is null)
        {
            throw new ArgumentNullException(nameof(workerId));
        }

        await using var command = await CreateCommandAsync(cancellationToken);
        command.CommandText = TinySqlServerEfCoreSql.ClaimPending(tableName);

        AddParameter(command, "@BatchSize", maxCount);
        AddParameter(command, "@WorkerId", workerId);
        AddParameter(command, "@Now", now);
        AddParameter(command, "@ClaimExpiresAtUtc", now.Add(claimTimeout));
        AddParameter(command, "@PendingStatus", (int)TinyOutboxMessageStatus.Pending);
        AddParameter(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadMessagesAsync(reader, cancellationToken);
    }

    public async ValueTask MarkProcessedAsync(
        Guid messageId,
        string workerId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken)
    {
        if (workerId is null)
        {
            throw new ArgumentNullException(nameof(workerId));
        }

        await using var command = await CreateCommandAsync(cancellationToken);
        command.CommandText = TinySqlServerEfCoreSql.MarkProcessed(tableName);

        AddParameter(command, "@Id", messageId);
        AddParameter(command, "@WorkerId", workerId);
        AddParameter(command, "@ProcessedAtUtc", processedAtUtc);
        AddParameter(command, "@ProcessedStatus", (int)TinyOutboxMessageStatus.Processed);
        AddParameter(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask MarkFailedAsync(
        Guid messageId,
        string workerId,
        string error,
        int attemptCount,
        DateTimeOffset? nextAttemptAtUtc,
        CancellationToken cancellationToken)
    {
        if (workerId is null)
        {
            throw new ArgumentNullException(nameof(workerId));
        }

        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        await using var command = await CreateCommandAsync(cancellationToken);
        command.CommandText = TinySqlServerEfCoreSql.MarkFailed(tableName);

        AddParameter(command, "@Id", messageId);
        AddParameter(command, "@WorkerId", workerId);
        AddParameter(command, "@Status", (int)GetFailedStatus(nextAttemptAtUtc));
        AddParameter(command, "@AttemptCount", attemptCount);
        AddParameter(command, "@NextAttemptAtUtc", nextAttemptAtUtc);
        AddParameter(command, "@LastError", error);
        AddParameter(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<DbCommand> CreateCommandAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        var currentTransaction = dbContext.Database.CurrentTransaction;

        if (currentTransaction is not null)
        {
            command.Transaction = currentTransaction.GetDbTransaction();
        }

        return command;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static async ValueTask<IReadOnlyList<TinyOutboxMessage>> ReadMessagesAsync(
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        var messages = new List<TinyOutboxMessage>();

        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    private static TinyOutboxMessage ReadMessage(DbDataReader reader)
    {
        return new TinyOutboxMessage
        {
            Id = reader.GetGuid(0),
            EventType = reader.GetString(1),
            Payload = reader.GetString(2),
            Status = (TinyOutboxMessageStatus)reader.GetInt32(3),
            AttemptCount = reader.GetInt32(4),
            ClaimedBy = ReadNullableString(reader, 5),
            ClaimedAtUtc = ReadNullableDateTimeOffset(reader, 6),
            ClaimExpiresAtUtc = ReadNullableDateTimeOffset(reader, 7),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(8),
            NextAttemptAtUtc = ReadNullableDateTimeOffset(reader, 9),
            ProcessedAtUtc = ReadNullableDateTimeOffset(reader, 10),
            LastError = ReadNullableString(reader, 11)
        };
    }

    private static string? ReadNullableString(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static TinyOutboxMessageStatus GetFailedStatus(DateTimeOffset? nextAttemptAtUtc)
    {
        if (nextAttemptAtUtc is null)
        {
            return TinyOutboxMessageStatus.Failed;
        }

        return TinyOutboxMessageStatus.Pending;
    }
}
