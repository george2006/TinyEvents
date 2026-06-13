using Xunit;

namespace TinyEvents.PostgreSql.EntityFrameworkCore.Tests;

public sealed class TinyPostgreSqlEfCoreTableNameTests
{
    [Theory]
    [InlineData("TinyOutbox", null, "TinyOutbox")]
    [InlineData("public.TinyOutbox", "public", "TinyOutbox")]
    [InlineData("app.MyOutbox", "app", "MyOutbox")]
    public void Parse_reads_schema_and_table(string tableName, string? expectedSchema, string expectedTable)
    {
        var parsed = TinyPostgreSqlEfCoreTableName.Parse(tableName);

        Assert.Equal(expectedSchema, parsed.Schema);
        Assert.Equal(expectedTable, parsed.Table);
    }

    [Theory]
    [InlineData("TinyOutbox", "\"TinyOutbox\"")]
    [InlineData("public.TinyOutbox", "\"public\".\"TinyOutbox\"")]
    [InlineData("app.MyOutbox", "\"app\".\"MyOutbox\"")]
    public void To_postgre_sql_name_quotes_each_identifier(string tableName, string expected)
    {
        var parsed = TinyPostgreSqlEfCoreTableName.Parse(tableName);

        Assert.Equal(expected, parsed.ToPostgreSqlName());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".TinyOutbox")]
    [InlineData("app.")]
    [InlineData("app..TinyOutbox")]
    [InlineData("app.my.table")]
    [InlineData("app-1.TinyOutbox")]
    [InlineData("app.Tiny Outbox")]
    public void Parse_rejects_invalid_table_names(string? tableName)
    {
        Assert.Throws<ArgumentException>(() => TinyPostgreSqlEfCoreTableName.Parse(tableName!));
    }
}
