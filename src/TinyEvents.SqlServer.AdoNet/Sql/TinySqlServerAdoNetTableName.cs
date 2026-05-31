namespace TinyEvents.SqlServer.AdoNet;

public sealed class TinySqlServerAdoNetTableName
{
    private readonly string[] parts;

    private TinySqlServerAdoNetTableName(string value)
    {
        parts = value.Split('.');
    }

    public static TinySqlServerAdoNetTableName Parse(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name is required.", nameof(tableName));
        }

        var parsed = new TinySqlServerAdoNetTableName(tableName);
        parsed.Validate();
        return parsed;
    }

    public string ToSqlServerName()
    {
        return string.Join(".", parts.Select(part => $"[{part}]"));
    }

    private void Validate()
    {
        foreach (var part in parts)
        {
            ValidatePart(part);
        }
    }

    private static void ValidatePart(string part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            throw new ArgumentException("Table name contains an empty segment.");
        }

        foreach (var character in part)
        {
            if (!IsAllowed(character))
            {
                throw new ArgumentException("Table name can only contain letters, digits, underscores, and dots.");
            }
        }
    }

    private static bool IsAllowed(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }
}
