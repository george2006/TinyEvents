using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TinyEvents.Migrations.SqlServer;

internal sealed class SqlServerMigrationLock
{
    private const string LockDomain = "TinyEvents.Migrations.Lock.v1";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);

    private readonly string lockDisplayName;
    private readonly int lockTimeoutMilliseconds;

    internal SqlServerMigrationLock(SqlServerMigrationTableIdentity tableIdentity)
        : this(tableIdentity, DefaultTimeout)
    {
    }

    internal SqlServerMigrationLock(
        SqlServerMigrationTableIdentity tableIdentity,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(tableIdentity);

        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The SQL Server migration-lock timeout must be positive and fit in milliseconds.");
        }

        Resource = CreateResource(tableIdentity);
        lockDisplayName = $"{tableIdentity.Schema}.{tableIdentity.HistoryTable}";
        lockTimeoutMilliseconds = checked((int)timeout.TotalMilliseconds);
    }

    internal string Resource { get; }

    internal async Task AcquireAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = (int)Math.Ceiling(lockTimeoutMilliseconds / 1000d) + 5;
        command.CommandText = """
            DECLARE @result int;

            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Session',
                @LockTimeout = @timeout;

            SELECT @result;
            """;
        AddParameter(command, "@resource", DbType.String, Resource);
        AddParameter(command, "@timeout", DbType.Int32, lockTimeoutMilliseconds);

        int returnCode;

        try
        {
            returnCode = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken));
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            await TryReleaseAfterCanceledAcquisitionAsync(connection);

            throw new OperationCanceledException(
                "SQL Server migration-lock acquisition was canceled by the caller.",
                exception,
                cancellationToken);
        }

        if (returnCode >= 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await TryReleaseAfterCanceledAcquisitionAsync(connection);
                throw new OperationCanceledException(cancellationToken);
            }

            return;
        }

        if (returnCode == -1)
        {
            throw new TimeoutException(
                $"Timed out after {TimeSpan.FromMilliseconds(lockTimeoutMilliseconds)} " +
                $"waiting for the TinyEvents migration lock for '{lockDisplayName}'.");
        }

        if (returnCode == -2 && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw new InvalidOperationException(
            $"SQL Server could not acquire the TinyEvents migration lock for " +
            $"'{lockDisplayName}'. sp_getapplock returned {returnCode}.");
    }

    internal async Task ReleaseAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;

            EXEC @result = sys.sp_releaseapplock
                @Resource = @resource,
                @LockOwner = N'Session';

            SELECT @result;
            """;
        AddParameter(command, "@resource", DbType.String, Resource);

        var returnCode = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken));

        if (returnCode < 0)
        {
            throw new InvalidOperationException(
                $"SQL Server could not release the TinyEvents migration lock for " +
                $"'{lockDisplayName}'. sp_releaseapplock returned {returnCode}.");
        }
    }

    private static string CreateResource(SqlServerMigrationTableIdentity tableIdentity)
    {
        var checksumInput = string.Concat(
            LockDomain,
            "\0",
            "sqlserver",
            "\0",
            tableIdentity.Schema.ToUpperInvariant(),
            "\0",
            tableIdentity.HistoryTable.ToUpperInvariant());
        var checksumBytes = Encoding.UTF8.GetBytes(checksumInput);
        var checksum = Convert.ToHexString(SHA256.HashData(checksumBytes));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{LockDomain}:{checksum}");
    }

    private async Task TryReleaseAfterCanceledAcquisitionAsync(DbConnection connection)
    {
        try
        {
            await ReleaseAsync(connection, CancellationToken.None);
        }
        catch
        {
            // The command may have been canceled before SQL Server acquired the lock.
            // Connection disposal remains the final fallback if acquisition raced cancellation.
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
