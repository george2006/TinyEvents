using System.Data.Common;

namespace TinyEvents.SqlServer.AdoNet;

public interface ITinySqlServerAdoNetWorkerConnectionFactory
{
    ValueTask<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
