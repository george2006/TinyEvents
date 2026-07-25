namespace TinyEvents.Worker;

public sealed class TinyEventsWorkerOptions
{
    private int batchSize = 50;
    private TimeSpan pollingInterval = TimeSpan.FromSeconds(5);
    private TimeSpan claimTimeout = TimeSpan.FromMinutes(5);
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

    public TimeSpan PollingInterval
    {
        get
        {
            return pollingInterval;
        }

        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Polling interval cannot be negative.");
            }

            pollingInterval = value;
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
}
