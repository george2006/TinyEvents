using Xunit;

namespace TinyEvents.SqlServer.Tests;

public sealed class SqlServerIntegrationFactAttribute : FactAttribute
{
    public SqlServerIntegrationFactAttribute()
    {
        if (!SqlServerIntegrationSettings.Enabled)
        {
            Skip = "Set TINYEVENTS_RUN_SQLSERVER_TESTS=true to run SQL Server integration tests.";
        }
    }
}

