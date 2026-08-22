using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TinyEvents.Migrations.PostgreSql;

internal sealed class PostgreSqlTinyEventsMigrator : ITinyEventsMigrator
{
    private readonly IPostgreSqlMigrationConnectionFactory connectionFactory;
    private readonly PostgreSqlMigrationTableIdentity tableIdentity;
    private readonly TinyEventsMigrationCatalog catalog;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;

    internal PostgreSqlTinyEventsMigrator(
        IPostgreSqlMigrationConnectionFactory connectionFactory,
        string configuredOutboxTable,
        TimeProvider timeProvider,
        ILogger? logger = null)
        : this(
            connectionFactory,
            PostgreSqlMigrationTableIdentity.Parse(configuredOutboxTable),
            timeProvider,
            catalog: null,
            logger)
    {
    }

    internal PostgreSqlTinyEventsMigrator(
        IPostgreSqlMigrationConnectionFactory connectionFactory,
        PostgreSqlMigrationTableIdentity tableIdentity,
        TimeProvider timeProvider,
        TinyEventsMigrationCatalog? catalog,
        ILogger? logger = null)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.tableIdentity = tableIdentity
            ?? throw new ArgumentNullException(nameof(tableIdentity));
        this.timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? NullLogger.Instance;
        this.catalog = catalog ?? new TinyEventsMigrationCatalog(
        [
            PostgreSqlMigration001CreateOutbox.Create(tableIdentity),
            PostgreSqlMigration002AddProcessedCleanupIndex.Create(tableIdentity)
        ]);
    }

    internal async Task MigrateAsync(CancellationToken cancellationToken)
    {
        const string provider = "postgresql";
        var startedAt = timeProvider.GetTimestamp();
        var previousVersion = 0L;
        var targetVersion = catalog.Migrations[^1].Version;
        var appliedCount = 0;
        TinyEventsMigrationLog.Started(
            logger,
            provider,
            tableIdentity.Schema,
            tableIdentity.OutboxTable);

        try
        {
            await using var migrationConnection =
                await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            var connection = migrationConnection.Connection;
            var history =
                new PostgreSqlMigrationHistory(tableIdentity, timeProvider);
            var migrationLock = new PostgreSqlMigrationLock(tableIdentity);
            var lockAcquired = false;

            try
            {
                await migrationLock.AcquireAsync(connection, cancellationToken);
                lockAcquired = true;

                await history.EnsureSchemaAsync(connection, cancellationToken);
                var historyExisted =
                    await history.ExistsAsync(connection, cancellationToken);
                await history.EnsureExistsAsync(connection, cancellationToken);

                if (!historyExisted)
                {
                    var outboxTableExists =
                        await history.OutboxTableExistsAsync(
                            connection,
                            cancellationToken);

                    if (outboxTableExists)
                    {
                        var baselineMigration = catalog.Migrations[0];
                        await RecordAlphaBaselineAsync(
                            connection,
                            history,
                            baselineMigration,
                            cancellationToken);
                        appliedCount++;
                        TinyEventsMigrationLog.Applied(
                            logger,
                            provider,
                            tableIdentity.Schema,
                            tableIdentity.OutboxTable,
                            baselineMigration.Version,
                            baselineMigration.Name,
                            isBaseline: true);
                    }
                }

                var appliedMigrations =
                    await history.ReadAsync(connection, cancellationToken);

                if (historyExisted && appliedMigrations.Count > 0)
                {
                    previousVersion = appliedMigrations[^1].Version;
                }

                var plan = new TinyEventsMigrationPlanner()
                    .CreatePlan(catalog, appliedMigrations);

                foreach (var migration in plan.PendingMigrations)
                {
                    await ApplyMigrationAsync(
                        connection,
                        history,
                        migration,
                        cancellationToken);
                    appliedCount++;
                    TinyEventsMigrationLog.Applied(
                        logger,
                        provider,
                        tableIdentity.Schema,
                        tableIdentity.OutboxTable,
                        migration.Version,
                        migration.Name,
                        isBaseline: false);
                }

                await EnsureSchemaIsCurrentAsync(
                    connection,
                    history,
                    cancellationToken);

                if (plan.IsCurrent)
                {
                    TinyEventsMigrationLog.SchemaCurrent(
                        logger,
                        provider,
                        tableIdentity.Schema,
                        tableIdentity.OutboxTable,
                        plan.CurrentVersion,
                        plan.TargetVersion);
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    await migrationLock.ReleaseAsync(
                        connection,
                        CancellationToken.None);
                }
            }

            TinyEventsMigrationLog.Completed(
                logger,
                provider,
                tableIdentity.Schema,
                tableIdentity.OutboxTable,
                previousVersion,
                targetVersion,
                appliedCount,
                timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TinyEventsMigrationLog.Failed(
                logger,
                provider,
                tableIdentity.Schema,
                tableIdentity.OutboxTable,
                timeProvider.GetElapsedTime(startedAt).TotalMilliseconds,
                exception);
            throw;
        }
    }

    Task ITinyEventsMigrator.MigrateAsync(CancellationToken cancellationToken)
    {
        return MigrateAsync(cancellationToken);
    }

    private static async Task RecordAlphaBaselineAsync(
        DbConnection connection,
        PostgreSqlMigrationHistory history,
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
        PostgreSqlMigrationHistory history,
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
        PostgreSqlMigrationHistory history,
        CancellationToken cancellationToken)
    {
        var finalHistory =
            await history.ReadAsync(connection, cancellationToken);
        var finalPlan =
            new TinyEventsMigrationPlanner().CreatePlan(catalog, finalHistory);

        if (!finalPlan.IsCurrent)
        {
            throw new InvalidOperationException(
                $"PostgreSQL migration completed at version " +
                $"{finalPlan.CurrentVersion}, but provider version " +
                $"{finalPlan.TargetVersion} is required.");
        }

        var outboxTableExists = await history.OutboxTableExistsAsync(
            connection,
            cancellationToken);

        if (!outboxTableExists)
        {
            throw new InvalidOperationException(
                $"PostgreSQL migration history is current at version " +
                $"{finalPlan.CurrentVersion}, but outbox table " +
                $"'{tableIdentity.Schema}.{tableIdentity.OutboxTable}' does not exist. " +
                "The schema is inconsistent.");
        }
    }
}
