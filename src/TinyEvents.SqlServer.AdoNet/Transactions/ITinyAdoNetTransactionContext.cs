using System.Data.Common;

namespace TinyEvents.SqlServer.AdoNet;

public interface ITinyAdoNetTransactionContext
{
    DbConnection Connection { get; }

    DbTransaction Transaction { get; }
}
