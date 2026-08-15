using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;

namespace TinyEvents.Migrations.PostgreSql;

internal sealed class PostgreSqlMigrationLock
{
    private const string LockDomain = "TinyEvents.Migrations.Lock.v1";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);

    private readonly string lockDisplayName;
    private readonly TimeSpan timeout;

    internal PostgreSqlMigrationLock(PostgreSqlMigrationTableIdentity tableIdentity)
        : this(tableIdentity, DefaultTimeout)
    {
    }

    internal PostgreSqlMigrationLock(
        PostgreSqlMigrationTableIdentity tableIdentity,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(tableIdentity);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The PostgreSQL migration-lock timeout must be positive.");
        }

        Key = CreateKey(tableIdentity);
        lockDisplayName = $"{tableIdentity.Schema}.{tableIdentity.HistoryTable}";
        this.timeout = timeout;
    }

    internal long Key { get; }

    internal async Task AcquireAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var acquisitionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_catalog.pg_advisory_lock(@key);";
            AddParameter(command, "@key", DbType.Int64, Key);
            await command.ExecuteScalarAsync(acquisitionCancellation.Token);
        }
        catch (Exception exception)
            when (cancellationToken.IsCancellationRequested)
        {
            await TryReleaseAfterCanceledAcquisitionAsync(connection);
            throw new OperationCanceledException(
                "PostgreSQL migration-lock acquisition was canceled by the caller.",
                exception,
                cancellationToken);
        }
        catch (Exception exception)
            when (timeoutCancellation.IsCancellationRequested)
        {
            await TryReleaseAfterCanceledAcquisitionAsync(connection);
            throw CreateTimeoutException(exception);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await TryReleaseAfterCanceledAcquisitionAsync(connection);
            throw new OperationCanceledException(cancellationToken);
        }

        if (timeoutCancellation.IsCancellationRequested)
        {
            await TryReleaseAfterCanceledAcquisitionAsync(connection);
            throw CreateTimeoutException();
        }
    }

    internal async Task ReleaseAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_catalog.pg_advisory_unlock(@key);";
        AddParameter(command, "@key", DbType.Int64, Key);
        var released = Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken));

        if (!released)
        {
            throw new InvalidOperationException(
                $"PostgreSQL could not release the TinyEvents migration lock for " +
                $"'{lockDisplayName}' because this session does not hold it.");
        }
    }

    private static long CreateKey(PostgreSqlMigrationTableIdentity tableIdentity)
    {
        var checksumInput = string.Concat(
            LockDomain,
            "\0",
            "postgresql",
            "\0",
            tableIdentity.Schema,
            "\0",
            tableIdentity.HistoryTable);
        var checksum = SHA256.HashData(Encoding.UTF8.GetBytes(checksumInput));

        return BinaryPrimitives.ReadInt64BigEndian(checksum);
    }

    private TimeoutException CreateTimeoutException(Exception? innerException = null)
    {
        return new TimeoutException(
            $"Timed out after {timeout} waiting for the TinyEvents migration lock for " +
            $"'{lockDisplayName}'.",
            innerException);
    }

    private async Task TryReleaseAfterCanceledAcquisitionAsync(DbConnection connection)
    {
        try
        {
            await ReleaseAsync(connection, CancellationToken.None);
        }
        catch
        {
            // The command may have been canceled before PostgreSQL acquired the lock.
            // Connection disposal remains the final fallback if cancellation raced acquisition.
        }
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
