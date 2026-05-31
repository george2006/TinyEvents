namespace TinyEvents.SqlServer.EntityFrameworkCore;

public sealed class TinySqlServerEfCoreTableName
{
    private readonly string[] parts;

    private TinySqlServerEfCoreTableName(string value)
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

    public static TinySqlServerEfCoreTableName Parse(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name is required.", nameof(tableName));
        }

        var parsed = new TinySqlServerEfCoreTableName(tableName);
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
            if (string.IsNullOrWhiteSpace(part))
            {
                throw new ArgumentException("Table name contains an empty segment.");
            }

            foreach (var character in part)
            {
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    throw new ArgumentException("Table name can only contain letters, digits, underscores, and dots.");
                }
            }
        }
    }
}
