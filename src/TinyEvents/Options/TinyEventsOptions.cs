namespace TinyEvents;

public sealed class TinyEventsOptions
{
    private readonly List<TinyEventNameAlias> eventNameAliases = new List<TinyEventNameAlias>();
    private int batchSize = 50;
    private int maxAttempts = 5;
    private TimeSpan retryDelay = TimeSpan.FromSeconds(30);
    private TimeSpan claimTimeout = TimeSpan.FromMinutes(5);
    private string? workerId;

    public int BatchSize
    {
        get
        {
            return batchSize;
        }

        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Batch size must be greater than zero.");
            }

            batchSize = value;
        }
    }

    public int MaxAttempts
    {
        get
        {
            return maxAttempts;
        }

        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum attempts must be greater than zero.");
            }

            maxAttempts = value;
        }
    }

    public TimeSpan RetryDelay
    {
        get
        {
            return retryDelay;
        }

        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Retry delay cannot be negative.");
            }

            retryDelay = value;
        }
    }

    public TimeSpan ClaimTimeout
    {
        get
        {
            return claimTimeout;
        }

        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Claim timeout must be greater than zero.");
            }

            claimTimeout = value;
        }
    }

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

    public void AcceptPreviousEventName<TEvent>(string previousEventName)
    {
        if (string.IsNullOrWhiteSpace(previousEventName))
        {
            throw new ArgumentException("Previous event name is required.", nameof(previousEventName));
        }

        eventNameAliases.Add(new TinyEventNameAlias(previousEventName, typeof(TEvent)));
    }

    internal IReadOnlyList<TinyEventNameAlias> EventNameAliases => eventNameAliases;

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
