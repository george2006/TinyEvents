namespace TinyEvents.Sample.PostgreSql.AdoNet.Infrastructure;

internal static class SampleSettings
{
    private const string EnvironmentVariable = "TINYEVENTS_SAMPLE_POSTGRESQL";

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

        return "Host=localhost;Port=54323;Database=tinyevents_samples;Username=postgres;Password=postgres;";
    }
}
