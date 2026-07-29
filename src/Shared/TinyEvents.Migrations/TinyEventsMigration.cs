using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TinyEvents.Migrations;

internal sealed record TinyEventsMigration
{
    private const string ChecksumDomain = "TinyEvents.Migrations.Checksum.v1";

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

        if (name.Contains('\0'))
        {
            throw new ArgumentException(
                "A migration name cannot contain the zero character.",
                nameof(name));
        }

        if (sql.Contains('\0'))
        {
            throw new ArgumentException(
                "Migration SQL cannot contain the zero character.",
                nameof(sql));
        }

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

    internal long Version { get; init; }

    internal string Name { get; init; }

    internal string Sql { get; init; }

    internal string Checksum
    {
        get
        {
            var version = Version.ToString(CultureInfo.InvariantCulture);
            var checksumInput = string.Concat(
                ChecksumDomain,
                "\0",
                version,
                "\0",
                Name,
                "\0",
                Sql);
            var checksumBytes = Encoding.UTF8.GetBytes(checksumInput);

            return Convert.ToHexString(SHA256.HashData(checksumBytes));
        }
    }

    private static bool IsAsciiLetter(char character)
    {
        return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
