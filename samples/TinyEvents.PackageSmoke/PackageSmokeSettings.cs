namespace TinyEvents.PackageSmoke;

internal static class PackageSmokeSettings
{
    private const string SqlServerEnvironmentVariable = "TINYEVENTS_PACKAGE_SMOKE_SQLSERVER";
    private const string PostgreSqlEnvironmentVariable = "TINYEVENTS_PACKAGE_SMOKE_POSTGRESQL";

    public static string GetSqlServerConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(SqlServerEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return "Server=localhost,14334;Database=TinyEventsPackageSmoke;User Id=sa;Password=TinyEvents_2026!;Encrypt=False;TrustServerCertificate=True;";
    }

    public static string GetPostgreSqlConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return "Host=localhost;Port=54324;Database=tinyevents_package_smoke;Username=postgres;Password=postgres;";
    }
}
