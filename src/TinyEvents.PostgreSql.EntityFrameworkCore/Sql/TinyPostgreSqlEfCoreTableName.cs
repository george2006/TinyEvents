namespace TinyEvents.PostgreSql.EntityFrameworkCore;

public sealed class TinyPostgreSqlEfCoreTableName
{
    private readonly string[] parts;

    private TinyPostgreSqlEfCoreTableName(string value)
    {
        parts = value.Split('.');
    }

    public string? Schema
    {
        get
        {
            if (parts.Length == 2)
            {
                return parts[0];
            }

            return null;
        }
    }

    public string Table => parts[parts.Length - 1];

    public static TinyPostgreSqlEfCoreTableName Parse(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name is required.", nameof(tableName));
        }

        var parsed = new TinyPostgreSqlEfCoreTableName(tableName);
        parsed.Validate();
        return parsed;
    }

    public string ToPostgreSqlName()
    {
        return string.Join(".", parts.Select(Quote));
    }

    private void Validate()
    {
        if (parts.Length > 2)
        {
            throw new ArgumentException("Table name can contain only table or schema.table.");
        }

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

    private static string Quote(string identifier)
    {
        return $"\"{identifier}\"";
    }
}
