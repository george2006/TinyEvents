using System.Data.Common;

namespace TinyEvents.Migrations.SqlServer;

internal interface ISqlServerMigrationConnectionFactory
{
    ValueTask<DbConnection> CreateOpenConnectionAsync(
        CancellationToken cancellationToken);
}
