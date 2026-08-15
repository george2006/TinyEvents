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

    [Fact]
    public void Constructor_rejects_a_zero_character_in_the_name()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigration(1, "001_Create\0TinyOutbox", "SELECT 1;"));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_zero_character_in_sql()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigration(1, "001_CreateTinyOutbox", "SELECT\0 1;"));

        Assert.Equal("sql", exception.ParamName);
    }

    [Theory]
    [InlineData(
        1,
        "001_CreateTinyOutbox",
        "CREATE TABLE TinyOutbox (Id uniqueidentifier NOT NULL);",
        "1931843A5C9AE9A443970EAEC827D4F646C4ABDEB7DAAD0E9EE81C2002FE1BE0")]
    [InlineData(
        2,
        "002_AddDeliveryPriority",
        "ALTER TABLE TinyOutbox\r\nADD DeliveryPriority int NOT NULL;",
        "C9BCFEA7FFE9741D1E8CFB8D7DA6BE2E32A5161CD4B725AD7E3B92B5708765A5")]
    [InlineData(
        1000,
        "1000_ExampleFutureMigration",
        "-- example\nSELECT 1;",
        "9CCFB24B585C73C4FFE282D04937E00D28E114A589CBDCA9D2B464B61CB5EFE1")]
    public void Checksum_matches_the_fixed_v1_vector(
        long version,
        string name,
        string sql,
        string expectedChecksum)
    {
        var migration = new TinyEventsMigration(version, name, sql);

        Assert.Equal(expectedChecksum, migration.Checksum);
        Assert.Equal(64, migration.Checksum.Length);
    }

    [Fact]
    public void Checksum_changes_when_the_version_changes()
    {
        var migration = new TinyEventsMigration(1, "001_CreateTinyOutbox", "SELECT 1;");
        var changedMigration = migration with { Version = 2 };

        Assert.NotEqual(migration.Checksum, changedMigration.Checksum);
    }

    [Fact]
    public void Checksum_changes_when_the_name_changes()
    {
        var migration = new TinyEventsMigration(1, "001_CreateTinyOutbox", "SELECT 1;");
        var changedMigration = migration with { Name = "001_CreateOutbox" };

        Assert.NotEqual(migration.Checksum, changedMigration.Checksum);
    }

    [Theory]
    [InlineData("SELECT  1;")]
    [InlineData("SELECT\n1;")]
    [InlineData("SELECT\r\n1;")]
    [InlineData("select 1;")]
    [InlineData("-- comment\nSELECT 1;")]
    public void Checksum_changes_when_sql_changes(string changedSql)
    {
        var migration = new TinyEventsMigration(1, "001_CreateTinyOutbox", "SELECT 1;");
        var changedMigration = new TinyEventsMigration(
            1,
            "001_CreateTinyOutbox",
            changedSql);

        Assert.NotEqual(migration.Checksum, changedMigration.Checksum);
    }
}
