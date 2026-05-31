namespace TinyEvents.PackageSmoke;

internal static class PackageSmokeSettings
{
    private const string EnvironmentVariable = "TINYEVENTS_PACKAGE_SMOKE_SQLSERVER";

    public static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return "Server=localhost,14334;Database=TinyEventsPackageSmoke;User Id=sa;Password=TinyEvents_2026!;Encrypt=False;TrustServerCertificate=True;";
    }
}
