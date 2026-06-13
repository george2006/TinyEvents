using System.Data.Common;

namespace TinyEvents.PostgreSql.AdoNet;

public sealed class TinyEventsPostgreSqlAdoNetOptions
{
    private Func<IServiceProvider, ITinyPostgreSqlAdoNetTransactionContext?>? currentTransaction;
    private Func<IServiceProvider, CancellationToken, ValueTask<DbConnection>>? workerConnectionFactory;

    public string TableName { get; set; } = "TinyOutbox";

    public void UseCurrentTransaction(
        Func<IServiceProvider, ITinyPostgreSqlAdoNetTransactionContext?> currentTransaction)
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

    internal ITinyPostgreSqlAdoNetTransactionContext? GetCurrentTransaction(IServiceProvider serviceProvider)
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

        if (workerConnectionFactory is null)
        {
            throw new InvalidOperationException(
                "An ADO.NET worker connection factory is required. Configure UseWorkerConnectionFactory(...) for outbox claiming and marking operations.");
        }

        return workerConnectionFactory(serviceProvider, cancellationToken);
    }
}
