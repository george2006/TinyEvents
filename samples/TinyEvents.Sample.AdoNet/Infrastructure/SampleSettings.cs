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

        throw new InvalidOperationException(
            $"Pass a SQL Server connection string as the first argument or set {EnvironmentVariable}.");
    }
}
