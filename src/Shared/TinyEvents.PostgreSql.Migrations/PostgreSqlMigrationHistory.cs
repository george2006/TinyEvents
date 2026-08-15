using System.Data;
using System.Data.Common;

namespace TinyEvents.Migrations.PostgreSql;

internal sealed class PostgreSqlMigrationHistory
{
    private readonly PostgreSqlMigrationTableIdentity tableIdentity;
    private readonly TimeProvider timeProvider;

    internal PostgreSqlMigrationHistory(
        PostgreSqlMigrationTableIdentity tableIdentity,
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
                await SchemaExistsAsync(
                    connection,
                    transaction,
                    cancellationToken);

            if (!schemaExists)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"CREATE SCHEMA {tableIdentity.QuotedSchema};";
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

    internal Task<bool> ExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return TableExistsAsync(
            connection,
            transaction: null,
            tableIdentity.HistoryTable,
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
                await TableExistsAsync(
                    connection,
                    transaction,
                    tableIdentity.HistoryTable,
                    cancellationToken);

            if (!historyTableExists)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    CREATE TABLE {tableIdentity.QuotedHistoryTable}
                    (
                        "Version" bigint NOT NULL,
                        "Name" text NOT NULL,
                        "Checksum" character(64) NOT NULL,
                        "AppliedAtUtc" timestamp with time zone NOT NULL,
                        CONSTRAINT {tableIdentity.QuotedHistoryPrimaryKey}
                            PRIMARY KEY ("Version")
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

    internal Task<bool> OutboxTableExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return TableExistsAsync(
            connection,
            transaction: null,
            tableIdentity.OutboxTable,
            cancellationToken);
    }

    internal async Task<IReadOnlyList<AppliedTinyEventsMigration>> ReadAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var migrations = new List<AppliedTinyEventsMigration>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT "Version", "Name", "Checksum", "AppliedAtUtc"
            FROM {tableIdentity.QuotedHistoryTable}
            ORDER BY "Version";
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(new AppliedTinyEventsMigration(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return migrations.AsReadOnly();
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
                ("Version", "Name", "Checksum", "AppliedAtUtc")
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
            SELECT EXISTS
            (
                SELECT 1
                FROM pg_catalog.pg_namespace
                WHERE nspname = @schema
            );
            """;
        AddParameter(command, "@schema", DbType.String, tableIdentity.Schema);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<bool> TableExistsAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS
            (
                SELECT 1
                FROM pg_catalog.pg_class AS tables
                INNER JOIN pg_catalog.pg_namespace AS schemas
                    ON schemas.oid = tables.relnamespace
                WHERE schemas.nspname = @schema
                  AND tables.relname = @table
                  AND tables.relkind IN ('r', 'p')
            );
            """;
        AddParameter(command, "@schema", DbType.String, tableIdentity.Schema);
        AddParameter(command, "@table", DbType.String, table);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
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
