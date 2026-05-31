namespace TinyEvents;

public interface ITinyOutboxProcessor
{
    ValueTask ProcessPendingAsync(CancellationToken cancellationToken = default);
}

