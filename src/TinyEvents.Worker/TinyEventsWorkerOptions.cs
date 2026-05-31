namespace TinyEvents.Worker;

public sealed class TinyEventsWorkerOptions
{
    private string? workerId;

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

    public int BatchSize { get; set; } = 50;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
