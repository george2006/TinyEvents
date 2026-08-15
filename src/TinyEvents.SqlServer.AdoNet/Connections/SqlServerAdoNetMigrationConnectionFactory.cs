using System.Data.Common;
using TinyEvents.Migrations.SqlServer;

namespace TinyEvents.SqlServer.AdoNet;

internal sealed class SqlServerAdoNetMigrationConnectionFactory
    : ISqlServerMigrationConnectionFactory
{
    private readonly ITinySqlServerAdoNetWorkerConnectionFactory workerConnectionFactory;

    public SqlServerAdoNetMigrationConnectionFactory(
        ITinySqlServerAdoNetWorkerConnectionFactory workerConnectionFactory)
    {
        this.workerConnectionFactory = workerConnectionFactory
            ?? throw new ArgumentNullException(nameof(workerConnectionFactory));
    }

    public async ValueTask<SqlServerMigrationConnection> CreateOpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection =
            await workerConnectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return new SqlServerMigrationConnection(
            connection,
            ownsConnection: true,
            closeWhenDisposed: false);
    }
}
