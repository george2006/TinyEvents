using System.Data.Common;

namespace TinyEvents.PostgreSql.AdoNet;

public interface ITinyPostgreSqlAdoNetWorkerConnectionFactory
{
    ValueTask<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
