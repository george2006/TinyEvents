using TinyEvents.Migrations.PostgreSql;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class PostgreSqlMigrationLockTests
{
    [Theory]
    [InlineData("TinyOutbox", -2176216172406286420)]
    [InlineData("app.MyOutbox", -3423757833270114159)]
    [InlineData("Sales_1.OrderEvents", 4901893486019449221)]
    public void Key_matches_the_fixed_vector(
        string configuredOutboxTable,
        long expectedKey)
    {
        var identity = PostgreSqlMigrationTableIdentity.Parse(configuredOutboxTable);

        var migrationLock = new PostgreSqlMigrationLock(identity);

        Assert.Equal(expectedKey, migrationLock.Key);
    }

    [Fact]
    public void Key_preserves_PostgreSQL_identifier_case()
    {
        var lowerCaseIdentity =
            PostgreSqlMigrationTableIdentity.Parse("app.events");
        var upperCaseIdentity =
            PostgreSqlMigrationTableIdentity.Parse("APP.EVENTS");

        Assert.NotEqual(
            new PostgreSqlMigrationLock(lowerCaseIdentity).Key,
            new PostgreSqlMigrationLock(upperCaseIdentity).Key);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_a_non_positive_timeout(int milliseconds)
    {
        var identity = PostgreSqlMigrationTableIdentity.Parse("TinyOutbox");

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PostgreSqlMigrationLock(
                identity,
                TimeSpan.FromMilliseconds(milliseconds)));

        Assert.Equal("timeout", exception.ParamName);
    }
}
