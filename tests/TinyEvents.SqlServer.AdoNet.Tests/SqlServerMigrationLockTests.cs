using TinyEvents.Migrations.SqlServer;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class SqlServerMigrationLockTests
{
    [Theory]
    [InlineData(
        "TinyOutbox",
        "TinyEvents.Migrations.Lock.v1:7595E38D4459D71AF9FE3DF27511D797940966A6BD7C09887E328845C613525C")]
    [InlineData(
        "app.MyOutbox",
        "TinyEvents.Migrations.Lock.v1:918754EC72DF5128F2FFF370ECB37171BDD605612DEF63C9A7AC1C1545D7B7BA")]
    [InlineData(
        "Sales_1.OrderEvents",
        "TinyEvents.Migrations.Lock.v1:255DA9060219C9C9352618E9F74DF0AFBECE670499FE973C5419107B74493437")]
    public void Resource_matches_the_fixed_vector(
        string configuredOutboxTable,
        string expectedResource)
    {
        var identity = SqlServerMigrationTableIdentity.Parse(configuredOutboxTable);

        var migrationLock = new SqlServerMigrationLock(identity);

        Assert.Equal(expectedResource, migrationLock.Resource);
    }

    [Fact]
    public void Resource_normalizes_SQL_Server_identifier_case()
    {
        var lowerCaseIdentity = SqlServerMigrationTableIdentity.Parse("app.events");
        var upperCaseIdentity = SqlServerMigrationTableIdentity.Parse("APP.EVENTS");

        var lowerCaseResource = new SqlServerMigrationLock(lowerCaseIdentity).Resource;
        var upperCaseResource = new SqlServerMigrationLock(upperCaseIdentity).Resource;

        Assert.Equal(lowerCaseResource, upperCaseResource);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_a_non_positive_timeout(int milliseconds)
    {
        var identity = SqlServerMigrationTableIdentity.Parse("TinyOutbox");

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SqlServerMigrationLock(
                identity,
                TimeSpan.FromMilliseconds(milliseconds)));

        Assert.Equal("timeout", exception.ParamName);
    }
}
