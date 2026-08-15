using System.Data.Common;

namespace TinyEvents.SqlServer.AdoNet;

public sealed class TinyEventsSqlServerAdoNetOptions
{
    private Func<IServiceProvider, ITinyAdoNetTransactionContext?>? currentTransaction;
    private Func<IServiceProvider, CancellationToken, ValueTask<DbConnection>>? workerConnectionFactory;

    public string TableName { get; set; } = "TinyOutbox";

    public void UseCurrentTransaction(
        Func<IServiceProvider, ITinyAdoNetTransactionContext?> currentTransaction)
    {
        this.currentTransaction = currentTransaction
            ?? throw new ArgumentNullException(nameof(currentTransaction));
    }

    public void UseWorkerConnectionFactory(
        Func<IServiceProvider, CancellationToken, ValueTask<DbConnection>> workerConnectionFactory)
    {
        this.workerConnectionFactory = workerConnectionFactory
            ?? throw new ArgumentNullException(nameof(workerConnectionFactory));
    }

    internal ITinyAdoNetTransactionContext? GetCurrentTransaction(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        return currentTransaction?.Invoke(serviceProvider);
    }

    internal ValueTask<DbConnection> CreateWorkerConnectionAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        ValidateWorkerConfiguration();

        return workerConnectionFactory!(serviceProvider, cancellationToken);
    }

    internal void ValidateWorkerConfiguration()
    {
        if (workerConnectionFactory is null)
        {
            throw new InvalidOperationException(
                "An ADO.NET connection factory is required for outbox worker operations and TinyEvents migrations. Configure UseWorkerConnectionFactory(...).");
        }
    }
}
