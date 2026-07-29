using System.Globalization;

namespace TinyEvents.Migrations;

internal sealed record TinyEventsMigration
{
    internal TinyEventsMigration(long version, string name, string sql)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "A migration version must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var expectedNamePrefix = version.ToString("D3", CultureInfo.InvariantCulture) + "_";

        if (!name.StartsWith(expectedNamePrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A migration name must begin with '{expectedNamePrefix}'.",
                nameof(name));
        }

        var explanation = name.AsSpan(expectedNamePrefix.Length);

        if (explanation.IsEmpty || !IsAsciiLetter(explanation[0]))
        {
            throw new ArgumentException(
                "A migration name explanation must begin with an ASCII letter.",
                nameof(name));
        }

        foreach (var character in explanation)
        {
            if (!IsAsciiLetter(character) && !char.IsAsciiDigit(character))
            {
                throw new ArgumentException(
                    "A migration name explanation must contain only ASCII letters and digits.",
                    nameof(name));
            }
        }

        Version = version;
        Name = name;
        Sql = sql;
    }

    internal long Version { get; }

    internal string Name { get; }

    internal string Sql { get; }

    private static bool IsAsciiLetter(char character)
    {
        return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
