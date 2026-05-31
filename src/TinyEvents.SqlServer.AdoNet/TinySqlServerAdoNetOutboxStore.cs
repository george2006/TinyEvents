using System.Data;
using System.Data.Common;

namespace TinyEvents.SqlServer.AdoNet;

public sealed class TinySqlServerAdoNetOutboxStore : ITinyOutboxStore
{
    private readonly TinyEventsSqlServerAdoNetOptions options;
    private readonly ITinySqlServerAdoNetWorkerConnectionFactory connectionFactory;
    private readonly TinySqlServerAdoNetTableName tableName;

    public TinySqlServerAdoNetOutboxStore(
        TinyEventsSqlServerAdoNetOptions options,
        ITinySqlServerAdoNetWorkerConnectionFactory connectionFactory)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (connectionFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        this.options = options;
        this.connectionFactory = connectionFactory;
        tableName = TinySqlServerAdoNetTableName.Parse(options.TableName);
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

        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = TinySqlServerAdoNetSql.ClaimPending(tableName);

        TinySqlServerAdoNetCommandParameters.Add(command, "@BatchSize", maxCount);
        TinySqlServerAdoNetCommandParameters.Add(command, "@WorkerId", workerId);
        TinySqlServerAdoNetCommandParameters.Add(command, "@Now", now);
        TinySqlServerAdoNetCommandParameters.Add(command, "@ClaimExpiresAtUtc", now.Add(claimTimeout));
        TinySqlServerAdoNetCommandParameters.Add(command, "@PendingStatus", (int)TinyOutboxMessageStatus.Pending);
        TinySqlServerAdoNetCommandParameters.Add(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

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

        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = TinySqlServerAdoNetSql.MarkProcessed(tableName);

        TinySqlServerAdoNetCommandParameters.Add(command, "@Id", messageId);
        TinySqlServerAdoNetCommandParameters.Add(command, "@WorkerId", workerId);
        TinySqlServerAdoNetCommandParameters.Add(command, "@ProcessedAtUtc", processedAtUtc);
        TinySqlServerAdoNetCommandParameters.Add(command, "@ProcessedStatus", (int)TinyOutboxMessageStatus.Processed);
        TinySqlServerAdoNetCommandParameters.Add(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

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

        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = TinySqlServerAdoNetSql.MarkFailed(tableName);

        TinySqlServerAdoNetCommandParameters.Add(command, "@Id", messageId);
        TinySqlServerAdoNetCommandParameters.Add(command, "@WorkerId", workerId);
        TinySqlServerAdoNetCommandParameters.Add(command, "@Status", (int)GetFailedStatus(nextAttemptAtUtc));
        TinySqlServerAdoNetCommandParameters.Add(command, "@AttemptCount", attemptCount);
        TinySqlServerAdoNetCommandParameters.Add(command, "@NextAttemptAtUtc", nextAttemptAtUtc);
        TinySqlServerAdoNetCommandParameters.Add(command, "@LastError", error);
        TinySqlServerAdoNetCommandParameters.Add(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        return await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
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
