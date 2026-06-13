using System.Data;
using System.Data.Common;

namespace TinyEvents.PostgreSql.AdoNet;

public sealed class TinyPostgreSqlAdoNetWorkerConnectionFactory : ITinyPostgreSqlAdoNetWorkerConnectionFactory
{
    private readonly TinyEventsPostgreSqlAdoNetOptions options;
    private readonly IServiceProvider serviceProvider;

    public TinyPostgreSqlAdoNetWorkerConnectionFactory(
        TinyEventsPostgreSqlAdoNetOptions options,
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
