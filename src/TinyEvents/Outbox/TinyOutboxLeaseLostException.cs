namespace TinyEvents;

internal sealed class TinyOutboxLeaseLostException : InvalidOperationException
{
    public TinyOutboxLeaseLostException(
        Guid messageId,
        string workerId,
        string operation)
        : base($"Outbox message '{messageId}' could not be marked as {operation} because worker '{workerId}' no longer owns a processing lease.")
    {
    }
}
