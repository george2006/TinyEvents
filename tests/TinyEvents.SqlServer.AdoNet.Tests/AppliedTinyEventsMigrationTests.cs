using TinyEvents.Migrations;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class AppliedTinyEventsMigrationTests
{
    [Fact]
    public void Constructor_accepts_applied_migration_history()
    {
        var appliedAtUtc = new DateTimeOffset(2026, 7, 29, 12, 30, 0, TimeSpan.Zero);

        var migration = new AppliedTinyEventsMigration(
            1,
            "001_CreateTinyOutbox",
            "RECORDEDCHECKSUM",
            appliedAtUtc);

        Assert.Equal(1, migration.Version);
        Assert.Equal("001_CreateTinyOutbox", migration.Name);
        Assert.Equal("RECORDEDCHECKSUM", migration.Checksum);
        Assert.Equal(appliedAtUtc, migration.AppliedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_a_non_positive_version(long version)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AppliedTinyEventsMigration(
                version,
                "001_CreateTinyOutbox",
                "RECORDEDCHECKSUM",
                DateTimeOffset.UnixEpoch));

        Assert.Equal("version", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_null_name()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new AppliedTinyEventsMigration(
                1,
                null!,
                "RECORDEDCHECKSUM",
                DateTimeOffset.UnixEpoch));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_rejects_an_empty_name(string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new AppliedTinyEventsMigration(
                1,
                name,
                "RECORDEDCHECKSUM",
                DateTimeOffset.UnixEpoch));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_null_checksum()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new AppliedTinyEventsMigration(
                1,
                "001_CreateTinyOutbox",
                null!,
                DateTimeOffset.UnixEpoch));

        Assert.Equal("checksum", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_rejects_an_empty_checksum(string checksum)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new AppliedTinyEventsMigration(
                1,
                "001_CreateTinyOutbox",
                checksum,
                DateTimeOffset.UnixEpoch));

        Assert.Equal("checksum", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_non_utc_timestamp()
    {
        var nonUtcTimestamp = new DateTimeOffset(
            2026,
            7,
            29,
            12,
            30,
            0,
            TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(
            () => new AppliedTinyEventsMigration(
                1,
                "001_CreateTinyOutbox",
                "RECORDEDCHECKSUM",
                nonUtcTimestamp));

        Assert.Equal("appliedAtUtc", exception.ParamName);
    }
}
