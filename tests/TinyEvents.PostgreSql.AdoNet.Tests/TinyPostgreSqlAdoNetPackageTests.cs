using System.Reflection;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class TinyPostgreSqlAdoNetPackageTests
{
    [Fact]
    public void Provider_assembly_loads()
    {
        var assembly = Assembly.Load("TinyEvents.PostgreSql.AdoNet");

        Assert.Equal("TinyEvents.PostgreSql.AdoNet", assembly.GetName().Name);
    }
}
