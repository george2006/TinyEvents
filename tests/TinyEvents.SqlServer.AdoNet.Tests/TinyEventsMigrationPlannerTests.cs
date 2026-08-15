using TinyEvents.Migrations;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class TinyEventsMigrationPlannerTests
{
    private static readonly DateTimeOffset AppliedAtUtc = DateTimeOffset.UnixEpoch;

    [Fact]
    public void CreatePlan_returns_every_migration_for_empty_history()
    {
        var catalog = Catalog();

        var plan = new TinyEventsMigrationPlanner().CreatePlan(catalog, []);

        Assert.Equal(0, plan.CurrentVersion);
        Assert.Equal(3, plan.TargetVersion);
        Assert.Equal(catalog.Migrations, plan.PendingMigrations);
        Assert.False(plan.IsCurrent);
    }

    [Fact]
    public void CreatePlan_returns_only_pending_migrations_for_partial_history()
    {
        var catalog = Catalog();
        var appliedMigrations = new[]
        {
            Applied(catalog.Migrations[0]),
            Applied(catalog.Migrations[1])
        };

        var plan = new TinyEventsMigrationPlanner().CreatePlan(catalog, appliedMigrations);

        Assert.Equal(2, plan.CurrentVersion);
        Assert.Equal(3, plan.TargetVersion);
        Assert.Equal([catalog.Migrations[2]], plan.PendingMigrations);
        Assert.False(plan.IsCurrent);
    }

    [Fact]
    public void CreatePlan_returns_a_current_plan_for_complete_history()
    {
        var catalog = Catalog();
        var appliedMigrations = catalog.Migrations.Select(Applied);

        var plan = new TinyEventsMigrationPlanner().CreatePlan(catalog, appliedMigrations);

        Assert.Equal(3, plan.CurrentVersion);
        Assert.Equal(3, plan.TargetVersion);
        Assert.Empty(plan.PendingMigrations);
        Assert.True(plan.IsCurrent);
    }

    [Fact]
    public void CreatePlan_orders_coherent_applied_history()
    {
        var catalog = Catalog();
        var appliedMigrations = new[]
        {
            Applied(catalog.Migrations[1]),
            Applied(catalog.Migrations[0])
        };

        var plan = new TinyEventsMigrationPlanner().CreatePlan(catalog, appliedMigrations);

        Assert.Equal(2, plan.CurrentVersion);
        Assert.Equal([catalog.Migrations[2]], plan.PendingMigrations);
    }

    [Fact]
    public void CreatePlan_rejects_a_database_newer_than_the_provider_first()
    {
        var catalog = Catalog();
        var appliedMigrations = new[]
        {
            Applied(catalog.Migrations[0]),
            Applied(catalog.Migrations[1]),
            Applied(catalog.Migrations[2]),
            new AppliedTinyEventsMigration(
                4,
                "wrong name",
                "wrong checksum",
                AppliedAtUtc)
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TinyEventsMigrationPlanner().CreatePlan(catalog, appliedMigrations));

        Assert.Contains("database migration version 4", exception.Message);
        Assert.Contains("provider migration version 3", exception.Message);
        Assert.DoesNotContain("wrong name", exception.Message);
    }

    [Fact]
    public void CreatePlan_rejects_history_that_does_not_start_at_version_one()
    {
        var catalog = Catalog();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TinyEventsMigrationPlanner().CreatePlan(
                catalog,
                [Applied(catalog.Migrations[1])]));

        Assert.Contains("Expected version 1, but found version 2", exception.Message);
    }

    [Fact]
    public void CreatePlan_rejects_a_history_gap()
    {
        var catalog = Catalog();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TinyEventsMigrationPlanner().CreatePlan(
                catalog,
                [
                    Applied(catalog.Migrations[0]),
                    Applied(catalog.Migrations[2])
                ]));

        Assert.Contains("Expected version 2, but found version 3", exception.Message);
    }

    [Fact]
    public void CreatePlan_rejects_a_duplicate_applied_version()
    {
        var catalog = Catalog();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TinyEventsMigrationPlanner().CreatePlan(
                catalog,
                [
                    Applied(catalog.Migrations[0]),
                    Applied(catalog.Migrations[0])
                ]));

        Assert.Contains("Expected version 2, but found version 1", exception.Message);
    }

    [Fact]
    public void CreatePlan_rejects_an_applied_name_mismatch_before_the_checksum()
    {
        var catalog = Catalog();
        var mismatchedMigration = new AppliedTinyEventsMigration(
            1,
            "001_RenamedMigration",
            "wrong checksum",
            AppliedAtUtc);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TinyEventsMigrationPlanner().CreatePlan(catalog, [mismatchedMigration]));

        Assert.Contains("001_RenamedMigration", exception.Message);
        Assert.Contains("001_CreateTinyOutbox", exception.Message);
        Assert.DoesNotContain("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreatePlan_rejects_an_applied_checksum_mismatch()
    {
        var catalog = Catalog();
        var catalogMigration = catalog.Migrations[0];
        var mismatchedMigration = new AppliedTinyEventsMigration(
            catalogMigration.Version,
            catalogMigration.Name,
            "wrong checksum",
            AppliedAtUtc);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TinyEventsMigrationPlanner().CreatePlan(catalog, [mismatchedMigration]));

        Assert.Contains("version 1", exception.Message);
        Assert.Contains("checksum", exception.Message);
    }

    [Fact]
    public void CreatePlan_rejects_null_applied_history()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TinyEventsMigrationPlanner().CreatePlan(Catalog(), null!));

        Assert.Equal("appliedMigrations", exception.ParamName);
    }

    [Fact]
    public void CreatePlan_rejects_a_null_applied_migration()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TinyEventsMigrationPlanner().CreatePlan(Catalog(), [null!]));

        Assert.Equal("appliedMigrations", exception.ParamName);
    }

    private static TinyEventsMigrationCatalog Catalog()
    {
        return new TinyEventsMigrationCatalog(
        [
            Migration(1, "001_CreateTinyOutbox"),
            Migration(2, "002_AddDeliveryPriority"),
            Migration(3, "003_AddLastAttemptAt")
        ]);
    }

    private static TinyEventsMigration Migration(long version, string name)
    {
        return new TinyEventsMigration(version, name, $"SELECT {version};");
    }

    private static AppliedTinyEventsMigration Applied(TinyEventsMigration migration)
    {
        return new AppliedTinyEventsMigration(
            migration.Version,
            migration.Name,
            migration.Checksum,
            AppliedAtUtc);
    }
}
