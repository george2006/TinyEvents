using TinyEvents.Migrations.PostgreSql;

namespace TinyEvents.PostgreSql.AdoNet;

internal sealed class PostgreSqlAdoNetMigrationConnectionFactory
    : IPostgreSqlMigrationConnectionFactory
{
    private readonly ITinyPostgreSqlAdoNetWorkerConnectionFactory
        workerConnectionFactory;

    public PostgreSqlAdoNetMigrationConnectionFactory(
        ITinyPostgreSqlAdoNetWorkerConnectionFactory workerConnectionFactory)
    {
        this.workerConnectionFactory = workerConnectionFactory
            ?? throw new ArgumentNullException(nameof(workerConnectionFactory));
    }

    public async ValueTask<PostgreSqlMigrationConnection>
        CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection =
            await workerConnectionFactory.CreateOpenConnectionAsync(
                cancellationToken);

        return new PostgreSqlMigrationConnection(
            connection,
            ownsConnection: true,
            closeWhenDisposed: false);
    }
}
