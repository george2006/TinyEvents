using System.Data.Common;

namespace TinyEvents.PostgreSql.AdoNet;

public sealed class TinyPostgreSqlAdoNetTransactionContext : ITinyPostgreSqlAdoNetTransactionContext
{
    public TinyPostgreSqlAdoNetTransactionContext(
        DbConnection connection,
        DbTransaction transaction)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public DbConnection Connection { get; }

    public DbTransaction Transaction { get; }
}
