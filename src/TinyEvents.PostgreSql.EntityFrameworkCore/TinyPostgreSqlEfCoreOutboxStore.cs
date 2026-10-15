using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TinyEvents.PostgreSql.EntityFrameworkCore;

internal sealed class TinyPostgreSqlEfCoreOutboxStore<TDbContext> :
    ITinyOutboxStore,
    ITinyOutboxCleanupStore,
    ITinyOutboxStatusReader
    where TDbContext : DbContext
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly TDbContext dbContext;
    private readonly TinyPostgreSqlEfCoreTableName tableName;
    private readonly TimeProvider timeProvider;

    public TinyPostgreSqlEfCoreOutboxStore(
        TDbContext dbContext,
        TinyEventsPostgreSqlEntityFrameworkCoreOptions options,
        TimeProvider timeProvider)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }

        ValidateDatabaseProvider(dbContext);

        this.dbContext = dbContext;
        tableName = TinyPostgreSqlEfCoreTableName.Parse(options.TableName);
        this.timeProvider = timeProvider;
    }

    public async ValueTask<TinyOutboxStatus> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedAtUtc = timeProvider.GetUtcNow();
        await using var command = await CreateCommandAsync(cancellationToken);
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
        command.CommandText = TinyPostgreSqlEfCoreSql.ReadStatus(tableName);
        AddParameter(command, "@ObservedAtUtc", observedAtUtc);
        AddParameter(command, "@PendingStatus", (int)TinyOutboxMessageStatus.Pending);
        AddParameter(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);
        AddParameter(command, "@FailedStatus", (int)TinyOutboxMessageStatus.Failed);
        AddParameter(command, "@ProcessedStatus", (int)TinyOutboxMessageStatus.Processed);
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

    private static void ValidateDatabaseProvider(TDbContext dbContext)
    {
        if (!string.Equals(
                dbContext.Database.ProviderName,
                PostgreSqlProviderName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The TinyEvents PostgreSQL EF Core provider requires the Npgsql EF Core provider. Configure the DbContext with UseNpgsql(...).");
        }
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
        command.CommandText = TinyPostgreSqlEfCoreSql.ClaimPending(tableName);

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
        command.CommandText = TinyPostgreSqlEfCoreSql.MarkProcessed(tableName);

        AddParameter(command, "@Id", messageId);
        AddParameter(command, "@WorkerId", workerId);
        AddParameter(command, "@ProcessedAtUtc", processedAtUtc);
        AddParameter(command, "@ProcessedStatus", (int)TinyOutboxMessageStatus.Processed);
        AddParameter(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

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

        await using var command = await CreateCommandAsync(cancellationToken);
        command.CommandText = TinyPostgreSqlEfCoreSql.MarkFailed(tableName);

        AddParameter(command, "@Id", messageId);
        AddParameter(command, "@WorkerId", workerId);
        AddParameter(command, "@Status", (int)GetFailedStatus(nextAttemptAtUtc));
        AddParameter(command, "@AttemptCount", attemptCount);
        AddParameter(command, "@NextAttemptAtUtc", nextAttemptAtUtc);
        AddParameter(command, "@LastError", error);
        AddParameter(command, "@ProcessingStatus", (int)TinyOutboxMessageStatus.Processing);

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

        await using var command = await CreateCommandAsync(cancellationToken);
        command.CommandText = TinyPostgreSqlEfCoreSql.DeleteProcessedBefore(tableName);

        AddParameter(command, "@BatchSize", maxCount);
        AddParameter(command, "@CutoffUtc", cutoffUtc);
        AddParameter(command, "@ProcessedStatus", (int)TinyOutboxMessageStatus.Processed);

        return await command.ExecuteNonQueryAsync(cancellationToken);
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
