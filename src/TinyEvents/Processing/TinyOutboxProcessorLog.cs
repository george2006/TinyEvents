using Microsoft.Extensions.Logging;

namespace TinyEvents;

internal static partial class TinyOutboxProcessorLog
{
    [LoggerMessage(
        EventId = 1202,
        EventName = "EventProcessingFailed",
        Level = LogLevel.Warning,
        Message = "TinyEvents outbox message {MessageId} for event type {EventType} failed processing on worker {WorkerId} at attempt {Attempt}. Next attempt at {NextAttemptAtUtc}.")]
    public static partial void ProcessingFailed(
        ILogger logger,
        Guid messageId,
        string eventType,
        string workerId,
        int attempt,
        DateTimeOffset nextAttemptAtUtc,
        Exception exception);

    [LoggerMessage(
        EventId = 1204,
        EventName = "EventRetriesExhausted",
        Level = LogLevel.Error,
        Message = "TinyEvents outbox message {MessageId} for event type {EventType} exhausted processing retries on worker {WorkerId} at attempt {Attempt} of {MaximumAttempts}.")]
    public static partial void RetriesExhausted(
        ILogger logger,
        Guid messageId,
        string eventType,
        string workerId,
        int attempt,
        int maximumAttempts,
        Exception exception);

    [LoggerMessage(
        EventId = 1300,
        EventName = "LeaseLost",
        Level = LogLevel.Warning,
        Message = "TinyEvents outbox message {MessageId} for event type {EventType} lost its processing lease while {Operation} on worker {WorkerId}.")]
    public static partial void LeaseLost(
        ILogger logger,
        Guid messageId,
        string eventType,
        string operation,
        string workerId,
        Exception exception);
}
