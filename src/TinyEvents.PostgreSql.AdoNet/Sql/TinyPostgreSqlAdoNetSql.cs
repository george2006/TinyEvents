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
}
