namespace TinyEvents;

public interface ITinyOutboxWriter
{
    ValueTask AddAsync(
        TinyOutboxMessage message,
        CancellationToken cancellationToken);
}

