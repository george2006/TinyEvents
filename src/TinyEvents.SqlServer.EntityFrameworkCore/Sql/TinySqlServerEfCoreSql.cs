namespace TinyEvents.SqlServer.EntityFrameworkCore;

public static class TinySqlServerEfCoreSql
{
    public static string ClaimPending(TinySqlServerEfCoreTableName tableName)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        return $"""
            WITH cte AS
            (
                SELECT TOP (@BatchSize) *
                FROM {tableName.ToSqlServerName()} WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE
                    (
                        Status = @PendingStatus
                        AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
                    )
                    OR
                    (
                        Status = @ProcessingStatus
                        AND ClaimExpiresAtUtc <= @Now
                    )
                ORDER BY CreatedAtUtc
            )
            UPDATE cte
            SET
                Status = @ProcessingStatus,
                ClaimedBy = @WorkerId,
                ClaimedAtUtc = @Now,
                ClaimExpiresAtUtc = @ClaimExpiresAtUtc
            OUTPUT
                INSERTED.Id,
                INSERTED.EventType,
                INSERTED.Payload,
                INSERTED.Status,
                INSERTED.AttemptCount,
                INSERTED.ClaimedBy,
                INSERTED.ClaimedAtUtc,
                INSERTED.ClaimExpiresAtUtc,
                INSERTED.CreatedAtUtc,
                INSERTED.NextAttemptAtUtc,
                INSERTED.ProcessedAtUtc,
                INSERTED.LastError;
            """;
    }

    public static string MarkProcessed(TinySqlServerEfCoreTableName tableName)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        return $"""
            UPDATE {tableName.ToSqlServerName()}
            SET
                Status = @ProcessedStatus,
                ProcessedAtUtc = @ProcessedAtUtc
            WHERE
                Id = @Id
                AND ClaimedBy = @WorkerId
                AND Status = @ProcessingStatus;
            """;
    }

    public static string MarkFailed(TinySqlServerEfCoreTableName tableName)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        return $"""
            UPDATE {tableName.ToSqlServerName()}
            SET
                Status = @Status,
                AttemptCount = @AttemptCount,
                NextAttemptAtUtc = @NextAttemptAtUtc,
                LastError = @LastError
            WHERE
                Id = @Id
                AND ClaimedBy = @WorkerId
                AND Status = @ProcessingStatus;
            """;
    }
}

