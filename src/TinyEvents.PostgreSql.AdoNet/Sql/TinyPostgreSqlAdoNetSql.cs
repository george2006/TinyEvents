namespace TinyEvents.PostgreSql.AdoNet;

public static class TinyPostgreSqlAdoNetSql
{
    public static string Insert(TinyPostgreSqlAdoNetTableName tableName)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        return $"""
            INSERT INTO {tableName.ToPostgreSqlName()}
            (
                "Id",
                "EventType",
                "Payload",
                "Status",
                "AttemptCount",
                "ClaimedBy",
                "ClaimedAtUtc",
                "ClaimExpiresAtUtc",
                "CreatedAtUtc",
                "NextAttemptAtUtc",
                "ProcessedAtUtc",
                "LastError"
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

    public static string ClaimPending(TinyPostgreSqlAdoNetTableName tableName)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        return $"""
            WITH claimed AS
            (
                SELECT "Id"
                FROM {tableName.ToPostgreSqlName()}
                WHERE
                    (
                        "Status" = @PendingStatus
                        AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= @Now)
                    )
                    OR
                    (
                        "Status" = @ProcessingStatus
                        AND "ClaimExpiresAtUtc" <= @Now
                    )
                ORDER BY "CreatedAtUtc"
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize
            )
            UPDATE {tableName.ToPostgreSqlName()} AS outbox
            SET
                "Status" = @ProcessingStatus,
                "ClaimedBy" = @WorkerId,
                "ClaimedAtUtc" = @Now,
                "ClaimExpiresAtUtc" = @ClaimExpiresAtUtc
            FROM claimed
            WHERE outbox."Id" = claimed."Id"
            RETURNING
                outbox."Id",
                outbox."EventType",
                outbox."Payload",
                outbox."Status",
                outbox."AttemptCount",
                outbox."ClaimedBy",
                outbox."ClaimedAtUtc",
                outbox."ClaimExpiresAtUtc",
                outbox."CreatedAtUtc",
                outbox."NextAttemptAtUtc",
                outbox."ProcessedAtUtc",
                outbox."LastError";
            """;
    }

    public static string MarkProcessed(TinyPostgreSqlAdoNetTableName tableName)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        return $"""
            UPDATE {tableName.ToPostgreSqlName()}
            SET
                "Status" = @ProcessedStatus,
                "ProcessedAtUtc" = @ProcessedAtUtc
            WHERE
                "Id" = @Id
                AND "ClaimedBy" = @WorkerId
                AND "Status" = @ProcessingStatus;
            """;
    }

    public static string MarkFailed(TinyPostgreSqlAdoNetTableName tableName)
    {
        if (tableName is null)
        {
            throw new ArgumentNullException(nameof(tableName));
        }

        return $"""
            UPDATE {tableName.ToPostgreSqlName()}
            SET
                "Status" = @Status,
                "AttemptCount" = @AttemptCount,
                "NextAttemptAtUtc" = @NextAttemptAtUtc,
                "LastError" = @LastError
            WHERE
                "Id" = @Id
                AND "ClaimedBy" = @WorkerId
                AND "Status" = @ProcessingStatus;
            """;
    }
}
