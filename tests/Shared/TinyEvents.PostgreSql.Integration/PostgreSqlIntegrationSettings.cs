namespace TinyEvents.PostgreSql.Integration;

public static class PostgreSqlIntegrationSettings
{
    public static bool Enabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("TINYEVENTS_RUN_POSTGRESQL_TESTS");
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
