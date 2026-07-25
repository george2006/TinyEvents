using System.Data.Common;

namespace TinyEvents.PostgreSql.AdoNet;

internal interface ITinyPostgreSqlAdoNetWorkerConnectionFactory
{
    ValueTask<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
