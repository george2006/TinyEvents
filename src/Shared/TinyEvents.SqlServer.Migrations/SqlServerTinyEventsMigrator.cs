using System.Data.Common;

namespace TinyEvents.Migrations.SqlServer;

internal sealed class SqlServerTinyEventsMigrator
{
    private readonly ISqlServerMigrationConnectionFactory connectionFactory;
    private readonly SqlServerMigrationTableIdentity tableIdentity;
    private readonly TinyEventsMigrationCatalog catalog;
    private readonly TimeProvider timeProvider;

    internal SqlServerTinyEventsMigrator(
        ISqlServerMigrationConnectionFactory connectionFactory,
        string configuredOutboxTable,
        TimeProvider timeProvider)
        : this(
            connectionFactory,
            SqlServerMigrationTableIdentity.Parse(configuredOutboxTable),
            timeProvider,
            catalog: null)
    {
    }

    internal SqlServerTinyEventsMigrator(
        ISqlServerMigrationConnectionFactory connectionFactory,
        SqlServerMigrationTableIdentity tableIdentity,
        TimeProvider timeProvider,
        TinyEventsMigrationCatalog? catalog)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.tableIdentity = tableIdentity
            ?? throw new ArgumentNullException(nameof(tableIdentity));
        this.timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        this.catalog = catalog ?? new TinyEventsMigrationCatalog(
        [
            SqlServerMigration001CreateOutbox.Create(tableIdentity)
        ]);
    }

    internal async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var history = new SqlServerMigrationHistory(tableIdentity, timeProvider);
        var migrationLock = new SqlServerMigrationLock(tableIdentity);
        var lockAcquired = false;

        try
        {
            await migrationLock.AcquireAsync(connection, cancellationToken);
            lockAcquired = true;

            await history.EnsureSchemaAsync(connection, cancellationToken);
            var historyExisted = await history.ExistsAsync(connection, cancellationToken);
            await history.EnsureExistsAsync(connection, cancellationToken);

            if (!historyExisted &&
                await history.OutboxTableExistsAsync(connection, cancellationToken))
            {
                await RecordAlphaBaselineAsync(
                    connection,
                    history,
                    catalog.Migrations[0],
                    cancellationToken);
            }

            var appliedMigrations = await history.ReadAsync(connection, cancellationToken);
            var plan = new TinyEventsMigrationPlanner().CreatePlan(catalog, appliedMigrations);

            foreach (var migration in plan.PendingMigrations)
            {
                await ApplyMigrationAsync(
                    connection,
                    history,
                    migration,
                    cancellationToken);
            }

            await EnsureSchemaIsCurrentAsync(connection, history, cancellationToken);
        }
        finally
        {
            if (lockAcquired)
            {
                await migrationLock.ReleaseAsync(connection, CancellationToken.None);
            }
        }
    }

    private static async Task RecordAlphaBaselineAsync(
        DbConnection connection,
        SqlServerMigrationHistory history,
        TinyEventsMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await history.AppendAsync(
                connection,
                transaction,
                migration,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task ApplyMigrationAsync(
        DbConnection connection,
        SqlServerMigrationHistory history,
        TinyEventsMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            await history.AppendAsync(
                connection,
                transaction,
                migration,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task EnsureSchemaIsCurrentAsync(
        DbConnection connection,
        SqlServerMigrationHistory history,
        CancellationToken cancellationToken)
    {
        var finalHistory = await history.ReadAsync(connection, cancellationToken);
        var finalPlan = new TinyEventsMigrationPlanner().CreatePlan(catalog, finalHistory);

        if (!finalPlan.IsCurrent)
        {
            throw new InvalidOperationException(
                $"SQL Server migration completed at version {finalPlan.CurrentVersion}, " +
                $"but provider version {finalPlan.TargetVersion} is required.");
        }
    }
}
