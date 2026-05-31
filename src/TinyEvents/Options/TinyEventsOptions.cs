namespace TinyEvents;

public sealed class TinyEventsOptions
{
    private string? workerId;

    public int BatchSize { get; set; } = 50;

    public int MaxAttempts { get; set; } = 5;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public string? WorkerId
    {
        get
        {
            return workerId;
        }

        set
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Worker id cannot be empty.", nameof(value));
            }

            workerId = value;
        }
    }

    internal string GetWorkerId()
    {
        if (workerId is not null)
        {
            return workerId;
        }

        workerId = CreateWorkerId();
        return workerId;
    }

    private static string CreateWorkerId()
    {
        return $"tiny-events-{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
    }
}
