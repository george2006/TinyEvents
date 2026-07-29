using TinyEvents.Migrations.SqlServer;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class SqlServerMigrationTableIdentityTests
{
    [Fact]
    public void Parse_derives_the_default_history_identity()
    {
        var identity = SqlServerMigrationTableIdentity.Parse("TinyOutbox");

        Assert.Equal("dbo", identity.Schema);
        Assert.Equal("TinyOutbox", identity.OutboxTable);
        Assert.Equal("TinyOutboxMigrations", identity.HistoryTable);
        Assert.Equal("PK_TinyOutbox", identity.OutboxPrimaryKey);
        Assert.Equal("IX_TinyOutbox_Pending", identity.PendingIndex);
        Assert.Equal("IX_TinyOutbox_ExpiredProcessing", identity.ExpiredProcessingIndex);
        Assert.Equal("IX_TinyOutbox_ClaimedBy", identity.ClaimedByIndex);
        Assert.Equal("PK_TinyOutboxMigrations", identity.HistoryPrimaryKey);
        Assert.Equal("[dbo].[TinyOutbox]", identity.QuotedOutboxTable);
        Assert.Equal("[dbo].[TinyOutboxMigrations]", identity.QuotedHistoryTable);
        Assert.Equal("[PK_TinyOutboxMigrations]", identity.QuotedHistoryPrimaryKey);
    }

    [Fact]
    public void Parse_preserves_a_custom_schema_and_table()
    {
        var identity = SqlServerMigrationTableIdentity.Parse("app.MyOutbox");

        Assert.Equal("app", identity.Schema);
        Assert.Equal("MyOutbox", identity.OutboxTable);
        Assert.Equal("MyOutboxMigrations", identity.HistoryTable);
        Assert.Equal("[app].[MyOutboxMigrations]", identity.QuotedHistoryTable);
    }

    [Fact]
    public void Parse_derives_independent_history_tables_for_two_outboxes()
    {
        var first = SqlServerMigrationTableIdentity.Parse("app.FirstOutbox");
        var second = SqlServerMigrationTableIdentity.Parse("app.SecondOutbox");

        Assert.NotEqual(first.HistoryTable, second.HistoryTable);
        Assert.Equal("[app].[FirstOutboxMigrations]", first.QuotedHistoryTable);
        Assert.Equal("[app].[SecondOutboxMigrations]", second.QuotedHistoryTable);
    }

    [Fact]
    public void Parse_rejects_a_derived_history_table_over_the_identifier_limit()
    {
        var outboxTable = new string('A', 119);

        var exception = Assert.Throws<ArgumentException>(
            () => SqlServerMigrationTableIdentity.Parse(outboxTable));

        Assert.Contains("migration-history table", exception.Message);
        Assert.Contains("128-character", exception.Message);
    }

    [Fact]
    public void Parse_rejects_a_derived_outbox_index_over_the_identifier_limit()
    {
        var outboxTable = new string('A', 116);

        var exception = Assert.Throws<ArgumentException>(
            () => SqlServerMigrationTableIdentity.Parse(outboxTable));

        Assert.Contains("expired-processing index", exception.Message);
        Assert.Contains("128-character", exception.Message);
    }
}
