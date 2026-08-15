using TinyEvents.Migrations;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class TinyEventsMigrationCatalogTests
{
    [Fact]
    public void Constructor_orders_migrations_and_exposes_the_latest_version()
    {
        var migration1 = Migration(1, "001_CreateTinyOutbox");
        var migration2 = Migration(2, "002_AddDeliveryPriority");

        var catalog = new TinyEventsMigrationCatalog([migration2, migration1]);

        Assert.Equal([migration1, migration2], catalog.Migrations);
        Assert.Equal(2, catalog.LatestVersion);
    }

    [Fact]
    public void Constructor_copies_the_supplied_collection()
    {
        var migration1 = Migration(1, "001_CreateTinyOutbox");
        var suppliedMigrations = new[] { migration1 };
        var catalog = new TinyEventsMigrationCatalog(suppliedMigrations);

        suppliedMigrations[0] = Migration(1, "001_Replacement");

        Assert.Same(migration1, catalog.Migrations[0]);
    }

    [Fact]
    public void Constructor_rejects_a_null_collection()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TinyEventsMigrationCatalog(null!));

        Assert.Equal("migrations", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_an_empty_collection()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigrationCatalog([]));

        Assert.Equal("migrations", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_null_migration()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigrationCatalog([null!]));

        Assert.Equal("migrations", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_catalog_that_does_not_start_at_version_one()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigrationCatalog(
                [Migration(2, "002_AddDeliveryPriority")]));

        Assert.Equal("migrations", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_version_gap()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigrationCatalog(
            [
                Migration(1, "001_CreateTinyOutbox"),
                Migration(3, "003_AddLastAttemptAt")
            ]));

        Assert.Equal("migrations", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_duplicate_versions()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigrationCatalog(
            [
                Migration(1, "001_CreateTinyOutbox"),
                Migration(1, "001_CreateReplacementOutbox")
            ]));

        Assert.Equal("migrations", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_duplicate_names_using_ordinal_comparison()
    {
        var migration1 = Migration(1, "001_CreateTinyOutbox");
        var migration2 = Migration(2, "002_AddDeliveryPriority");
        var duplicateName = migration2 with { Name = migration1.Name };

        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigrationCatalog([migration1, duplicateName]));

        Assert.Equal("migrations", exception.ParamName);
    }

    private static TinyEventsMigration Migration(long version, string name)
    {
        return new TinyEventsMigration(version, name, "SELECT 1;");
    }
}
