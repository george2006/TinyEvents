using System.Data;
using System.Data.Common;

namespace TinyEvents.Migrations.SqlServer;

internal sealed class SqlServerMigrationConnection : IAsyncDisposable
{
    private readonly bool ownsConnection;
    private readonly bool closeWhenDisposed;

    internal SqlServerMigrationConnection(
        DbConnection connection,
        bool ownsConnection,
        bool closeWhenDisposed)
    {
        Connection = connection
            ?? throw new ArgumentNullException(nameof(connection));
        this.ownsConnection = ownsConnection;
        this.closeWhenDisposed = closeWhenDisposed;
    }

    internal DbConnection Connection { get; }

    public async ValueTask DisposeAsync()
    {
        if (ownsConnection)
        {
            await Connection.DisposeAsync();
            return;
        }

        if (closeWhenDisposed && Connection.State != ConnectionState.Closed)
        {
            await Connection.CloseAsync();
        }
    }
}
