namespace TinyEvents.Sample.AdoNet.Infrastructure;

internal static class SampleSettings
{
    private const string EnvironmentVariable = "TINYEVENTS_SAMPLE_SQLSERVER";

    public static string GetConnectionString(string[] args)
    {
        if (args.Length > 0)
        {
            return args[0];
        }

        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return "Server=localhost,14333;Database=TinyEventsSamples;User Id=sa;Password=TinyEvents_2026!;Encrypt=False;TrustServerCertificate=True;";
    }
}
