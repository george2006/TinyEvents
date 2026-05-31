namespace TinyEvents.SqlServer.AdoNet;

public static class TinySqlServerAdoNetSql
{
    public static string Insert(TinySqlServerAdoNetTableName tableName)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        return $"""
            INSERT INTO {tableName.ToSqlServerName()}
            (
                Id,
                EventType,
                Payload,
                Status,
                AttemptCount,
                ClaimedBy,
                ClaimedAtUtc,
                ClaimExpiresAtUtc,
                CreatedAtUtc,
                NextAttemptAtUtc,
                ProcessedAtUtc,
                LastError
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
                @CreatedAtUtc,
                @NextAttemptAtUtc,
                @ProcessedAtUtc,
                @LastError
            );
            """;
    }

    public static string ClaimPending(TinySqlServerAdoNetTableName tableName)
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

    public static string MarkProcessed(TinySqlServerAdoNetTableName tableName)
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

    public static string MarkFailed(TinySqlServerAdoNetTableName tableName)
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

