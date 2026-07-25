using System.Data.Common;

namespace TinyEvents.SqlServer.AdoNet;

internal interface ITinySqlServerAdoNetWorkerConnectionFactory
{
    ValueTask<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
