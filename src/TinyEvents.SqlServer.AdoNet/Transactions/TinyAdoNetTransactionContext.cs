using System.Data.Common;

namespace TinyEvents.SqlServer.AdoNet;

public sealed class TinyAdoNetTransactionContext : ITinyAdoNetTransactionContext
{
    public TinyAdoNetTransactionContext(
        DbConnection connection,
        DbTransaction transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public DbConnection Connection { get; }

    public DbTransaction Transaction { get; }
}
