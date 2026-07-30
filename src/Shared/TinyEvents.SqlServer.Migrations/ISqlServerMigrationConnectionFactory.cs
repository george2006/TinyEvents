using System.Data.Common;

namespace TinyEvents.Migrations.SqlServer;

internal interface ISqlServerMigrationConnectionFactory
{
    ValueTask<SqlServerMigrationConnection> CreateOpenConnectionAsync(
        CancellationToken cancellationToken);
}
