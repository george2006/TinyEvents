namespace TinyEvents;

public interface ITinyOutboxStatusReader
{
    ValueTask<TinyOutboxStatus> ReadAsync(CancellationToken cancellationToken = default);
}
