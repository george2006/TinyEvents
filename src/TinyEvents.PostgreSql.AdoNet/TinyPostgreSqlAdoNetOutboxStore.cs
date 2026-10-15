using System.Data.Common;

namespace TinyEvents.PostgreSql.AdoNet;

internal sealed class TinyPostgreSqlAdoNetOutboxStore :
    ITinyOutboxStore,
    ITinyOutboxCleanupStore,
    ITinyOutboxStatusReader
{
    private readonly ITinyPostgreSqlAdoNetWorkerConnectionFactory connectionFactory;
    private readonly TinyPostgreSqlAdoNetTableName tableName;
    private readonly TimeProvider timeProvider;

    public TinyPostgreSqlAdoNetOutboxStore(
        TinyEventsPostgreSqlAdoNetOptions options,
        ITinyPostgreSqlAdoNetWorkerConnectionFactory connectionFactory,
        TimeProvider timeProvider)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (connectionFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }

        this.timeProvider = timeProvider;
        this.connectionFactory = connectionFactory;
        tableName = TinyPostgreSqlAdoNetTableName.Parse(options.TableName);
    }

    public async ValueTask<TinyOutboxStatus> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedAtUtc = timeProvider.GetUtcNow();
        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        ConfigureStatusQuery(command, observedAtUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The outbox status query did not return an observation.");
        }

        return ReadStatus(reader, observedAtUtc);
    }

    private void ConfigureStatusQuery(DbCommand command, DateTimeOffset observedAtUtc)
    {
        command.CommandText = TinyPostgreSqlAdoNetSql.ReadStatus(tableName);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ObservedAtUtc", observedAtUtc);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@PendingStatus", (int)TinyOutboxMessageStatus.Pending);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@FailedStatus", (int)TinyOutboxMessageStatus.Failed);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ProcessedStatus", (int)TinyOutboxMessageStatus.Processed);
    }

    private static TinyOutboxStatus ReadStatus(DbDataReader reader, DateTimeOffset observedAtUtc)
    {
        return new TinyOutboxStatus
        {
            ObservedAtUtc = observedAtUtc,
            PendingCount = reader.GetInt64(0),
            ProcessingCount = reader.GetInt64(1),
            ExpiredProcessingCount = reader.GetInt64(2),
            FailedCount = reader.GetInt64(3),
            OldestPendingAtUtc = ReadNullableDateTimeOffset(reader, 4),
            LastProcessedAtUtc = ReadNullableDateTimeOffset(reader, 5)
        };
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
        command.CommandText = TinyPostgreSqlAdoNetSql.ClaimPending(tableName);

        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@BatchSize", maxCount);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@WorkerId", workerId);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@Now", now);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ClaimExpiresAtUtc", now.Add(claimTimeout));
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@PendingStatus", (int)TinyOutboxMessageStatus.Pending);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

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
        command.CommandText = TinyPostgreSqlAdoNetSql.MarkProcessed(tableName);

        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@Id", messageId);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@WorkerId", workerId);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ProcessedAtUtc", processedAtUtc);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ProcessedStatus", (int)TinyOutboxMessageStatus.Processed);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        ThrowIfLeaseWasLost(affectedRows, messageId, workerId, "processed");
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
        command.CommandText = TinyPostgreSqlAdoNetSql.MarkFailed(tableName);

        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@Id", messageId);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@WorkerId", workerId);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@Status", (int)GetFailedStatus(nextAttemptAtUtc));
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@AttemptCount", attemptCount);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@NextAttemptAtUtc", nextAttemptAtUtc);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@LastError", error);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        ThrowIfLeaseWasLost(affectedRows, messageId, workerId, "failed");
    }

    public async ValueTask<int> DeleteProcessedBeforeAsync(
        DateTimeOffset cutoffUtc,
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCount),
                "Maximum cleanup count must be greater than zero.");
        }

        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = TinyPostgreSqlAdoNetSql.DeleteProcessedBefore(tableName);

        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@BatchSize", maxCount);
        TinyPostgreSqlAdoNetCommandParameters.Add(command, "@CutoffUtc", cutoffUtc);
        TinyPostgreSqlAdoNetCommandParameters.Add(
            command,
            "@ProcessedStatus",
            (int)TinyOutboxMessageStatus.Processed);

        return await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static void ThrowIfLeaseWasLost(
        int affectedRows,
        Guid messageId,
        string workerId,
        string operation)
    {
        if (affectedRows == 0)
        {
            throw new TinyOutboxLeaseLostException(messageId, workerId, operation);
        }
    }
}
