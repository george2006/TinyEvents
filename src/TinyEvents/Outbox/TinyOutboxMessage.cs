namespace TinyEvents;

public sealed class TinyOutboxMessage
{
    public Guid Id { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string Payload { get; init; } = string.Empty;

    public TinyOutboxMessageStatus Status { get; init; }

    public int AttemptCount { get; init; }

    public string? ClaimedBy { get; init; }

    public DateTimeOffset? ClaimedAtUtc { get; init; }

    public DateTimeOffset? ClaimExpiresAtUtc { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? NextAttemptAtUtc { get; init; }

    public DateTimeOffset? ProcessedAtUtc { get; init; }

    public string? LastError { get; init; }
}

