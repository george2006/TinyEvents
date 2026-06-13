namespace TinyEvents.PostgreSql.AdoNet;

public sealed class TinyPostgreSqlAdoNetTableName
{
    private readonly string[] parts;

    private TinyPostgreSqlAdoNetTableName(string value)
    {
        parts = value.Split('.');
    }

    public static TinyPostgreSqlAdoNetTableName Parse(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name is required.", nameof(tableName));
        }

        var parsed = new TinyPostgreSqlAdoNetTableName(tableName);
        parsed.Validate();
        return parsed;
    }

    public string ToPostgreSqlName()
    {
        return string.Join(".", parts.Select(Quote));
    }

    public string ToPostgreSqlName(string defaultSchema)
    {
        if (string.IsNullOrWhiteSpace(defaultSchema))
        {
            throw new ArgumentException("Default schema is required.", nameof(defaultSchema));
        }

        return parts.Length == 1
            ? $"{Quote(defaultSchema)}.{Quote(parts[0])}"
            : ToPostgreSqlName();
    }

    public string ToPostgreSqlObjectName(string defaultSchema = "public")
    {
        if (string.IsNullOrWhiteSpace(defaultSchema))
        {
            throw new ArgumentException("Default schema is required.", nameof(defaultSchema));
        }

        return parts.Length == 1
            ? $"{defaultSchema}.{parts[0]}"
            : string.Join(".", parts);
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
