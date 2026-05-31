using System.Data;
using System.Data.Common;

namespace TinyEvents.SqlServer.AdoNet;

public sealed class TinySqlServerAdoNetWorkerConnectionFactory : ITinySqlServerAdoNetWorkerConnectionFactory
{
    private readonly TinyEventsSqlServerAdoNetOptions options;
    private readonly IServiceProvider serviceProvider;

    public TinySqlServerAdoNetWorkerConnectionFactory(
        TinyEventsSqlServerAdoNetOptions options,
        IServiceProvider serviceProvider)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        this.options = options;
        this.serviceProvider = serviceProvider;
    }

    public async ValueTask<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await options.CreateWorkerConnectionAsync(serviceProvider, cancellationToken);

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }
}
