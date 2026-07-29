using TinyEvents.Migrations;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class TinyEventsMigrationTests
{
    [Theory]
    [InlineData(1, "001_CreateTinyOutbox")]
    [InlineData(2, "002_AddDeliveryPriority")]
    [InlineData(1000, "1000_ExampleFutureMigration")]
    public void Constructor_accepts_a_valid_migration(long version, string name)
    {
        var migration = new TinyEventsMigration(version, name, "SELECT 1;");

        Assert.Equal(version, migration.Version);
        Assert.Equal(name, migration.Name);
        Assert.Equal("SELECT 1;", migration.Sql);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_a_non_positive_version(long version)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TinyEventsMigration(version, "001_CreateTinyOutbox", "SELECT 1;"));

        Assert.Equal("version", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_null_name()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TinyEventsMigration(1, null!, "SELECT 1;"));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_rejects_an_empty_name(string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigration(1, name, "SELECT 1;"));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData(1, "1_CreateTinyOutbox")]
    [InlineData(1, "0001_CreateTinyOutbox")]
    [InlineData(2, "001_CreateTinyOutbox")]
    [InlineData(1000, "01000_CreateTinyOutbox")]
    public void Constructor_rejects_a_name_without_the_exact_version_prefix(
        long version,
        string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigration(version, name, "SELECT 1;"));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("001_")]
    [InlineData("001_1CreateTinyOutbox")]
    [InlineData("001_Événement")]
    public void Constructor_rejects_an_explanation_that_does_not_begin_with_an_ascii_letter(
        string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigration(1, name, "SELECT 1;"));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("001_Create_TinyOutbox")]
    [InlineData("001_Create-TinyOutbox")]
    [InlineData("001_Create TinyOutbox")]
    [InlineData("001_CreateÉvénement")]
    public void Constructor_rejects_non_alphanumeric_explanation_characters(string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigration(1, name, "SELECT 1;"));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_null_sql()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TinyEventsMigration(1, "001_CreateTinyOutbox", null!));

        Assert.Equal("sql", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_rejects_empty_sql(string sql)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigration(1, "001_CreateTinyOutbox", sql));

        Assert.Equal("sql", exception.ParamName);
    }
}
