using System.Data;
using Microsoft.EntityFrameworkCore;
using TinyEvents.Migrations.SqlServer;

namespace TinyEvents.SqlServer.EntityFrameworkCore;

internal sealed class SqlServerEfCoreMigrationConnectionFactory<TDbContext>
    : ISqlServerMigrationConnectionFactory
    where TDbContext : DbContext
{
    private const string SqlServerProviderName =
        "Microsoft.EntityFrameworkCore.SqlServer";

    private readonly TDbContext dbContext;

    public SqlServerEfCoreMigrationConnectionFactory(TDbContext dbContext)
    {
        this.dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));

        if (!string.Equals(
                dbContext.Database.ProviderName,
                SqlServerProviderName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The TinyEvents SQL Server EF Core migration adapter requires the " +
                "SQL Server EF Core provider. Configure the DbContext with UseSqlServer(...).");
        }
    }

    public async ValueTask<SqlServerMigrationConnection> CreateOpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var connectionWasClosed = connection.State == ConnectionState.Closed;

        if (connectionWasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return new SqlServerMigrationConnection(
            connection,
            ownsConnection: false,
            closeWhenDisposed: connectionWasClosed);
    }
}
