using System.Data;
using Microsoft.EntityFrameworkCore;
using TinyEvents.Migrations.PostgreSql;

namespace TinyEvents.PostgreSql.EntityFrameworkCore;

internal sealed class PostgreSqlEfCoreMigrationConnectionFactory<TDbContext>
    : IPostgreSqlMigrationConnectionFactory
    where TDbContext : DbContext
{
    private const string PostgreSqlProviderName =
        "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly TDbContext dbContext;

    public PostgreSqlEfCoreMigrationConnectionFactory(TDbContext dbContext)
    {
        this.dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));

        if (!string.Equals(
                dbContext.Database.ProviderName,
                PostgreSqlProviderName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The TinyEvents PostgreSQL EF Core migration adapter requires " +
                "the Npgsql EF Core provider. Configure the DbContext with " +
                "UseNpgsql(...).");
        }
    }

    public async ValueTask<PostgreSqlMigrationConnection>
        CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var connectionWasClosed = connection.State == ConnectionState.Closed;

        if (connectionWasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return new PostgreSqlMigrationConnection(
            connection,
            ownsConnection: false,
            closeWhenDisposed: connectionWasClosed);
    }
}
