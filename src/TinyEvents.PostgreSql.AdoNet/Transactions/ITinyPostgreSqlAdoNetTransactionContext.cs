using System.Data.Common;

namespace TinyEvents.PostgreSql.AdoNet;

public interface ITinyPostgreSqlAdoNetTransactionContext
{
    DbConnection Connection { get; }

    DbTransaction Transaction { get; }
}
