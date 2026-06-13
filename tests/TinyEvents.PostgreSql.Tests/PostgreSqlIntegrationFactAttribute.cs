using Xunit;

namespace TinyEvents.PostgreSql.Tests;

public sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlIntegrationFactAttribute()
    {
        if (!PostgreSqlIntegrationSettings.Enabled)
        {
            Skip = "Set TINYEVENTS_RUN_POSTGRESQL_TESTS=true to run PostgreSQL integration tests.";
        }
    }
}
