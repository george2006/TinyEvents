using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class TinyPostgreSqlAdoNetTableNameTests
{
    [Theory]
    [InlineData("TinyOutbox", "\"TinyOutbox\"")]
    [InlineData("public.TinyOutbox", "\"public\".\"TinyOutbox\"")]
    [InlineData("app.MyOutbox", "\"app\".\"MyOutbox\"")]
    public void To_postgre_sql_name_quotes_each_identifier(string tableName, string expected)
    {
        var parsed = TinyPostgreSqlAdoNetTableName.Parse(tableName);

        Assert.Equal(expected, parsed.ToPostgreSqlName());
    }

    [Theory]
    [InlineData("TinyOutbox", "\"public\".\"TinyOutbox\"")]
    [InlineData("app.MyOutbox", "\"app\".\"MyOutbox\"")]
    public void To_postgre_sql_name_applies_default_schema_to_unqualified_table(string tableName, string expected)
    {
        var parsed = TinyPostgreSqlAdoNetTableName.Parse(tableName);

        Assert.Equal(expected, parsed.ToPostgreSqlName("public"));
    }

    [Theory]
    [InlineData("TinyOutbox", "public.TinyOutbox")]
    [InlineData("app.MyOutbox", "app.MyOutbox")]
    public void To_postgre_sql_object_name_applies_default_schema_to_unqualified_table(string tableName, string expected)
    {
        var parsed = TinyPostgreSqlAdoNetTableName.Parse(tableName);

        Assert.Equal(expected, parsed.ToPostgreSqlObjectName());
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
        Assert.Throws<ArgumentException>(() => TinyPostgreSqlAdoNetTableName.Parse(tableName!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void To_postgre_sql_name_rejects_invalid_default_schema(string? defaultSchema)
    {
        var parsed = TinyPostgreSqlAdoNetTableName.Parse("TinyOutbox");

        Assert.Throws<ArgumentException>(() => parsed.ToPostgreSqlName(defaultSchema!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void To_postgre_sql_object_name_rejects_invalid_default_schema(string? defaultSchema)
    {
        var parsed = TinyPostgreSqlAdoNetTableName.Parse("TinyOutbox");

        Assert.Throws<ArgumentException>(() => parsed.ToPostgreSqlObjectName(defaultSchema!));
    }
}
