namespace TinyEvents.Migrations.PostgreSql;

internal interface IPostgreSqlMigrationConnectionFactory
{
    ValueTask<PostgreSqlMigrationConnection> CreateOpenConnectionAsync(
        CancellationToken cancellationToken);
}
