using Microsoft.Extensions.Logging;

namespace TinyEvents;

internal static partial class TinyOutboxProcessorLog
{
    public static void ProcessingFailure(
        ILogger logger,
        TinyOutboxMessage message,
        string workerId,
        int attemptCount,
        int maximumAttempts,
        DateTimeOffset? nextAttemptAtUtc,
        Exception exception)
    {
        if (nextAttemptAtUtc is null)
        {
            RetriesExhausted(
                logger,
                message.Id,
                message.EventType,
                workerId,
                attemptCount,
                maximumAttempts,
                exception);
            return;
        }

        ProcessingFailed(
            logger,
            message.Id,
            message.EventType,
            workerId,
            attemptCount,
            nextAttemptAtUtc.Value,
            exception);
    }

    public static void LeaseLost(
        ILogger logger,
        TinyOutboxMessage message,
        string workerId,
        string operation,
        Exception exception)
    {
        LeaseLost(
            logger,
            message.Id,
            message.EventType,
            operation,
            workerId,
            exception);
    }

    [LoggerMessage(
        EventId = 1202,
        EventName = "EventProcessingFailed",
        Level = LogLevel.Warning,
        Message = "TinyEvents outbox message {MessageId} for event type {EventType} failed processing on worker {WorkerId} at attempt {Attempt}. Next attempt at {NextAttemptAtUtc}.")]
    private static partial void ProcessingFailed(
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
    private static partial void RetriesExhausted(
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
    private static partial void LeaseLost(
        ILogger logger,
        Guid messageId,
        string eventType,
        string operation,
        string workerId,
        Exception exception);
}
