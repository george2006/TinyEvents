using System.Data;
using System.Data.Common;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class TinyPostgreSqlAdoNetOptionsTests
{
    [Fact]
    public void Options_use_current_transaction_rejects_null_delegate()
    {
        var options = new TinyEventsPostgreSqlAdoNetOptions();

        Assert.Throws<ArgumentNullException>(() => options.UseCurrentTransaction(null!));
    }

    [Fact]
    public void Options_use_worker_connection_factory_rejects_null_delegate()
    {
        var options = new TinyEventsPostgreSqlAdoNetOptions();

        Assert.Throws<ArgumentNullException>(() => options.UseWorkerConnectionFactory(null!));
    }

    [Fact]
    public void Transaction_context_rejects_null_connection()
    {
        var transaction = new RecordingTransaction(new RecordingConnection(ConnectionState.Open));

        Assert.Throws<ArgumentNullException>(() => new TinyPostgreSqlAdoNetTransactionContext(null!, transaction));
    }

    [Fact]
    public void Transaction_context_rejects_null_transaction()
    {
        var connection = new RecordingConnection(ConnectionState.Open);

        Assert.Throws<ArgumentNullException>(() => new TinyPostgreSqlAdoNetTransactionContext(connection, null!));
    }

    [Fact]
    public void Transaction_context_exposes_connection_and_transaction()
    {
        var connection = new RecordingConnection(ConnectionState.Open);
        var transaction = new RecordingTransaction(connection);

        var context = new TinyPostgreSqlAdoNetTransactionContext(connection, transaction);

        Assert.Same(connection, context.Connection);
        Assert.Same(transaction, context.Transaction);
    }

    [Fact]
    public async Task Worker_connection_factory_uses_configured_delegate()
    {
        var connection = new RecordingConnection(ConnectionState.Open);
        var options = new TinyEventsPostgreSqlAdoNetOptions();
        options.UseWorkerConnectionFactory((_, _) => new ValueTask<DbConnection>(connection));
        var factory = NewFactory(options);

        var result = await factory.CreateOpenConnectionAsync(CancellationToken.None);

        Assert.Same(connection, result);
    }

    [Fact]
    public async Task Worker_connection_factory_opens_closed_connection()
    {
        var connection = new RecordingConnection(ConnectionState.Closed);
        var options = new TinyEventsPostgreSqlAdoNetOptions();
        options.UseWorkerConnectionFactory((_, _) => new ValueTask<DbConnection>(connection));
        var factory = NewFactory(options);

        await factory.CreateOpenConnectionAsync(CancellationToken.None);

        Assert.Equal(1, connection.OpenCount);
    }

    [Fact]
    public async Task Worker_connection_factory_does_not_open_already_open_connection()
    {
        var connection = new RecordingConnection(ConnectionState.Open);
        var options = new TinyEventsPostgreSqlAdoNetOptions();
        options.UseWorkerConnectionFactory((_, _) => new ValueTask<DbConnection>(connection));
        var factory = NewFactory(options);

        await factory.CreateOpenConnectionAsync(CancellationToken.None);

        Assert.Equal(0, connection.OpenCount);
    }

    [Fact]
    public async Task Worker_connection_factory_fails_clearly_when_delegate_is_missing()
    {
        var factory = NewFactory(new TinyEventsPostgreSqlAdoNetOptions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await factory.CreateOpenConnectionAsync(CancellationToken.None));

        Assert.Contains("Configure UseWorkerConnectionFactory(...)", exception.Message);
    }

    [Fact]
    public void Worker_connection_factory_rejects_null_options()
    {
        var serviceProvider = new RecordingServiceProvider();

        Assert.Throws<ArgumentNullException>(() => new TinyPostgreSqlAdoNetWorkerConnectionFactory(null!, serviceProvider));
    }

    [Fact]
    public void Worker_connection_factory_rejects_null_service_provider()
    {
        var options = new TinyEventsPostgreSqlAdoNetOptions();

        Assert.Throws<ArgumentNullException>(() => new TinyPostgreSqlAdoNetWorkerConnectionFactory(options, null!));
    }

    private static TinyPostgreSqlAdoNetWorkerConnectionFactory NewFactory(
        TinyEventsPostgreSqlAdoNetOptions options)
    {
        return new TinyPostgreSqlAdoNetWorkerConnectionFactory(
            options,
            new RecordingServiceProvider());
    }

#nullable disable
    private sealed class RecordingServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class RecordingConnection : DbConnection
    {
        private ConnectionState state;

        public RecordingConnection(ConnectionState state)
        {
            this.state = state;
        }

        public int OpenCount { get; private set; }

        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "Test";

        public override string DataSource => "Test";

        public override string ServerVersion => "1";

        public override ConnectionState State => state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            state = ConnectionState.Closed;
        }

        public override void Open()
        {
            OpenCount++;
            state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Open();
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            return new RecordingTransaction(this);
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingTransaction : DbTransaction
    {
        private readonly DbConnection connection;

        public RecordingTransaction(DbConnection connection)
        {
            this.connection = connection;
        }

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection DbConnection => connection;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }
#nullable restore
}
