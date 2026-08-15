using System.Data;
using System.Data.Common;

namespace TinyEvents.Migrations.SqlServer;

internal sealed class SqlServerMigrationHistory
{
    private readonly SqlServerMigrationTableIdentity tableIdentity;
    private readonly TimeProvider timeProvider;

    internal SqlServerMigrationHistory(
        SqlServerMigrationTableIdentity tableIdentity,
        TimeProvider timeProvider)
    {
        this.tableIdentity = tableIdentity
            ?? throw new ArgumentNullException(nameof(tableIdentity));
        this.timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal async Task EnsureSchemaAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var schemaExists =
                await SchemaExistsAsync(connection, transaction, cancellationToken);

            if (!schemaExists)
            {
                await using var createCommand = connection.CreateCommand();
                createCommand.Transaction = transaction;
                createCommand.CommandText = $"CREATE SCHEMA {tableIdentity.QuotedSchema};";
                await createCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal async Task<bool> ExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return await HistoryTableExistsAsync(
            connection,
            transaction: null,
            cancellationToken);
    }

    internal async Task EnsureExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var historyTableExists =
                await HistoryTableExistsAsync(
                    connection,
                    transaction,
                    cancellationToken);

            if (!historyTableExists)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    CREATE TABLE {tableIdentity.QuotedHistoryTable}
                    (
                        [Version] bigint NOT NULL,
                        [Name] nvarchar(512) NOT NULL,
                        [Checksum] char(64) NOT NULL,
                        [AppliedAtUtc] datetimeoffset NOT NULL,
                        CONSTRAINT {tableIdentity.QuotedHistoryPrimaryKey}
                            PRIMARY KEY ([Version])
                    );
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal async Task<bool> OutboxTableExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM sys.tables AS tables
                INNER JOIN sys.schemas AS schemas
                    ON schemas.schema_id = tables.schema_id
                WHERE schemas.name = @schema
                  AND tables.name = @table
            )
            THEN 1
            ELSE 0
            END;
            """;
        AddParameter(command, "@schema", DbType.String, tableIdentity.Schema);
        AddParameter(command, "@table", DbType.String, tableIdentity.OutboxTable);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    internal async Task<IReadOnlyList<AppliedTinyEventsMigration>> ReadAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var appliedMigrations = new List<AppliedTinyEventsMigration>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Version], [Name], [Checksum], [AppliedAtUtc]
            FROM {tableIdentity.QuotedHistoryTable}
            ORDER BY [Version];
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            appliedMigrations.Add(
                new AppliedTinyEventsMigration(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return appliedMigrations.AsReadOnly();
    }

    internal async Task AppendAsync(
        DbConnection connection,
        DbTransaction transaction,
        TinyEventsMigration migration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(migration);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {tableIdentity.QuotedHistoryTable}
                ([Version], [Name], [Checksum], [AppliedAtUtc])
            VALUES
                (@version, @name, @checksum, @appliedAtUtc);
            """;
        var appliedAtUtc = timeProvider.GetUtcNow();
        AddParameter(command, "@version", DbType.Int64, migration.Version);
        AddParameter(command, "@name", DbType.String, migration.Name);
        AddParameter(command, "@checksum", DbType.AnsiStringFixedLength, migration.Checksum);
        AddParameter(command, "@appliedAtUtc", DbType.DateTimeOffset, appliedAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> SchemaExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM sys.schemas
                WHERE name = @schema
            )
            THEN 1
            ELSE 0
            END;
            """;
        AddParameter(command, "@schema", DbType.String, tableIdentity.Schema);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private async Task<bool> HistoryTableExistsAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM sys.tables AS tables
                INNER JOIN sys.schemas AS schemas
                    ON schemas.schema_id = tables.schema_id
                WHERE schemas.name = @schema
                  AND tables.name = @table
            )
            THEN 1
            ELSE 0
            END;
            """;
        AddParameter(command, "@schema", DbType.String, tableIdentity.Schema);
        AddParameter(command, "@table", DbType.String, tableIdentity.HistoryTable);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
