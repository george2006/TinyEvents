namespace TinyEvents.Migrations.SqlServer;

internal sealed class SqlServerMigrationTableIdentity
{
    private const int MaximumIdentifierLength = 128;

    private SqlServerMigrationTableIdentity(string schema, string outboxTable)
    {
        Schema = schema;
        OutboxTable = outboxTable;
        HistoryTable = outboxTable + "Migrations";
        OutboxPrimaryKey = "PK_" + outboxTable;
        PendingIndex = "IX_" + outboxTable + "_Pending";
        ExpiredProcessingIndex = "IX_" + outboxTable + "_ExpiredProcessing";
        ClaimedByIndex = "IX_" + outboxTable + "_ClaimedBy";
        HistoryPrimaryKey = "PK_" + HistoryTable;

        ValidateIdentifierLength(HistoryTable, "derived migration-history table");
        ValidateIdentifierLength(OutboxPrimaryKey, "derived outbox primary key");
        ValidateIdentifierLength(PendingIndex, "derived pending index");
        ValidateIdentifierLength(ExpiredProcessingIndex, "derived expired-processing index");
        ValidateIdentifierLength(ClaimedByIndex, "derived claimed-by index");
        ValidateIdentifierLength(HistoryPrimaryKey, "derived migration-history primary key");
    }

    internal string Schema { get; }

    internal string OutboxTable { get; }

    internal string HistoryTable { get; }

    internal string OutboxPrimaryKey { get; }

    internal string PendingIndex { get; }

    internal string ExpiredProcessingIndex { get; }

    internal string ClaimedByIndex { get; }

    internal string HistoryPrimaryKey { get; }

    internal string QuotedSchema => Quote(Schema);

    internal string QuotedOutboxTable => $"{QuotedSchema}.{Quote(OutboxTable)}";

    internal string QuotedHistoryTable => $"{QuotedSchema}.{Quote(HistoryTable)}";

    internal string QuotedOutboxPrimaryKey => Quote(OutboxPrimaryKey);

    internal string QuotedPendingIndex => Quote(PendingIndex);

    internal string QuotedExpiredProcessingIndex => Quote(ExpiredProcessingIndex);

    internal string QuotedClaimedByIndex => Quote(ClaimedByIndex);

    internal string QuotedHistoryPrimaryKey => Quote(HistoryPrimaryKey);

    internal static SqlServerMigrationTableIdentity Parse(string configuredOutboxTable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredOutboxTable);

        var parts = configuredOutboxTable.Split('.');

        if (parts.Length is < 1 or > 2)
        {
            throw new ArgumentException(
                "A SQL Server outbox table must contain a table name with an optional schema.",
                nameof(configuredOutboxTable));
        }

        foreach (var part in parts)
        {
            ValidateIdentifier(part, nameof(configuredOutboxTable));
        }

        var schema = parts.Length == 1 ? "dbo" : parts[0];
        var outboxTable = parts[^1];

        return new SqlServerMigrationTableIdentity(schema, outboxTable);
    }

    private static void ValidateIdentifier(string identifier, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException(
                "A SQL Server outbox table cannot contain an empty identifier.",
                parameterName);
        }

        ValidateIdentifierLength(identifier, "configured outbox identifier", parameterName);

        foreach (var character in identifier)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                throw new ArgumentException(
                    "A SQL Server outbox table can contain only letters, digits, underscores, and one optional dot.",
                    parameterName);
            }
        }
    }

    private static void ValidateIdentifierLength(
        string identifier,
        string description,
        string? parameterName = null)
    {
        if (identifier.Length > MaximumIdentifierLength)
        {
            throw new ArgumentException(
                $"The {description} '{identifier}' exceeds SQL Server's " +
                $"{MaximumIdentifierLength}-character identifier limit.",
                parameterName);
        }
    }

    private static string Quote(string identifier)
    {
        return $"[{identifier}]";
    }
}
