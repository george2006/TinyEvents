using System.Reflection;
using Xunit;

namespace TinyEvents.PostgreSql.EntityFrameworkCore.Tests;

public sealed class TinyPostgreSqlEntityFrameworkCorePackageTests
{
    [Fact]
    public void Provider_assembly_loads()
    {
        var assembly = Assembly.Load("TinyEvents.PostgreSql.EntityFrameworkCore");

        Assert.Equal("TinyEvents.PostgreSql.EntityFrameworkCore", assembly.GetName().Name);
    }
}
