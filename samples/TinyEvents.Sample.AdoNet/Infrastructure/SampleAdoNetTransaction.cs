using System.Data.Common;

namespace TinyEvents.Sample.AdoNet.Infrastructure;

public sealed class SampleAdoNetTransaction : IAsyncDisposable
{
    public SampleAdoNetTransaction(
        DbConnection connection,
        DbTransaction transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public DbConnection Connection { get; }

    public DbTransaction Transaction { get; }

    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        await Transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        await Transaction.RollbackAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
