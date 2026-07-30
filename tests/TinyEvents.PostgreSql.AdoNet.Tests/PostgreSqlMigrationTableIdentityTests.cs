using TinyEvents.Migrations.PostgreSql;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class PostgreSqlMigrationTableIdentityTests
{
    [Fact]
    public void Parse_uses_public_and_preserves_exact_table_case()
    {
        var identity = PostgreSqlMigrationTableIdentity.Parse("TinyOutbox");

        Assert.Equal("public", identity.Schema);
        Assert.Equal("TinyOutbox", identity.OutboxTable);
        Assert.Equal("TinyOutboxMigrations", identity.HistoryTable);
        Assert.Equal("\"public\".\"TinyOutboxMigrations\"", identity.QuotedHistoryTable);
    }

    [Fact]
    public void Parse_preserves_exact_schema_case()
    {
        var identity = PostgreSqlMigrationTableIdentity.Parse("AppData.Events");

        Assert.Equal("AppData", identity.Schema);
        Assert.Equal("Events", identity.OutboxTable);
    }

    [Fact]
    public void Parse_rejects_a_derived_name_over_the_identifier_limit()
    {
        var table = new string('a', 54);

        var exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlMigrationTableIdentity.Parse(table));

        Assert.Contains("63-byte identifier limit", exception.Message);
    }

    [Fact]
    public void Parse_validates_migration_001_index_names_before_execution()
    {
        var table = new string('a', 50);

        var exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlMigrationTableIdentity.Parse(table));

        Assert.Contains("derived expired-processing index", exception.Message);
        Assert.Contains("63-byte identifier limit", exception.Message);
    }

    [Fact]
    public void Parse_measures_the_identifier_limit_in_utf8_bytes()
    {
        var table = new string('\u00E9', 32);

        var exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlMigrationTableIdentity.Parse(table));

        Assert.Contains("63-byte identifier limit", exception.Message);
    }
}
