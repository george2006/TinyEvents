using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class TinyPostgreSqlAdoNetSchemaTests
{
    [Fact]
    public void Schema_helper_creates_default_outbox_sql()
    {
        var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql();

        Assert.Contains("CREATE SCHEMA IF NOT EXISTS \"public\"", sql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"public\".\"TinyOutbox\"", sql);
        Assert.Contains("\"Id\" uuid NOT NULL", sql);
        Assert.Contains("\"EventType\" text NOT NULL", sql);
        Assert.Contains("\"Status\" integer NOT NULL", sql);
        Assert.Contains("\"ClaimedAtUtc\" timestamp with time zone NULL", sql);
        Assert.Contains("\"IX_TinyOutbox_Pending\"", sql);
        Assert.Contains("\"IX_TinyOutbox_ExpiredProcessing\"", sql);
        Assert.Contains("\"IX_TinyOutbox_ClaimedBy\"", sql);
    }

    [Fact]
    public void Schema_helper_creates_custom_table_outbox_sql()
    {
        var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql("app.MyOutbox");

        Assert.Contains("CREATE SCHEMA IF NOT EXISTS \"app\"", sql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"app\".\"MyOutbox\"", sql);
        Assert.Contains("ON \"app\".\"MyOutbox\"", sql);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("TinyOutbox;DROP TABLE Users")]
    [InlineData("public..TinyOutbox")]
    [InlineData("public.Tiny-Outbox")]
    [InlineData("server.public.TinyOutbox")]
    public void Schema_helper_rejects_unsafe_table_names(string tableName)
    {
        Assert.Throws<ArgumentException>(() => TinyPostgreSqlAdoNetSchema.CreateOutboxSql(tableName));
    }
}
