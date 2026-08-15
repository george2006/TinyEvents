namespace TinyEvents.Migrations;

internal sealed record AppliedTinyEventsMigration
{
    internal AppliedTinyEventsMigration(
        long version,
        string name,
        string checksum,
        DateTimeOffset appliedAtUtc)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "An applied migration version must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);

        if (appliedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "An applied migration timestamp must use the UTC offset.",
                nameof(appliedAtUtc));
        }

        Version = version;
        Name = name;
        Checksum = checksum;
        AppliedAtUtc = appliedAtUtc;
    }

    internal long Version { get; }

    internal string Name { get; }

    internal string Checksum { get; }

    internal DateTimeOffset AppliedAtUtc { get; }
}
