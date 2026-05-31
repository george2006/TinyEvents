namespace TinyEvents.SqlServer.Tests;

public static class SqlServerIntegrationSettings
{
    public static bool Enabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("TINYEVENTS_RUN_SQLSERVER_TESTS");
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}

