using System.Data;
using System.Data.Common;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

public sealed class TinyPostgreSqlAdoNetWriterTests
{
    [Fact]
    public void Writer_rejects_null_options()
    {
        var serviceProvider = new RecordingServiceProvider();

        Assert.Throws<ArgumentNullException>(() => new TinyPostgreSqlAdoNetOutboxWriter(null!, serviceProvider));
    }

    [Fact]
    public void Writer_rejects_null_service_provider()
    {
        var options = new TinyEventsPostgreSqlAdoNetOptions();

        Assert.Throws<ArgumentNullException>(() => new TinyPostgreSqlAdoNetOutboxWriter(options, null!));
    }

    [Fact]
    public async Task Add_async_rejects_null_message()
    {
        var writer = NewWriter(new RecordingConnection(), new RecordingTransaction(new RecordingConnection()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await writer.AddAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Add_async_fails_clearly_if_current_transaction_is_not_configured()
    {
        var writer = new TinyPostgreSqlAdoNetOutboxWriter(
            new TinyEventsPostgreSqlAdoNetOptions(),
            new RecordingServiceProvider());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.AddAsync(NewMessage(), CancellationToken.None));

        Assert.Contains("Configure UseCurrentTransaction(...)", exception.Message);
    }

    [Fact]
    public async Task Add_async_uses_configured_current_transaction_context()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var writer = NewWriter(connection, transaction);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Same(transaction, connection.LastCommand!.Transaction);
        Assert.Contains("INSERT INTO \"TinyOutbox\"", connection.LastCommand.CommandText);
        Assert.True(connection.LastCommand.Parameters.Contains("@Payload"));
    }

    [Fact]
    public async Task Add_async_uses_custom_table_name()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var options = NewOptions(connection, transaction);
        options.TableName = "app.MyOutbox";
        var writer = new TinyPostgreSqlAdoNetOutboxWriter(options, new RecordingServiceProvider());

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Contains("INSERT INTO \"app\".\"MyOutbox\"", connection.LastCommand!.CommandText);
    }

    [Fact]
    public async Task Add_async_does_not_use_worker_connection_factory()
    {
        var workerFactoryCalls = 0;
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var options = NewOptions(connection, transaction);
        options.UseWorkerConnectionFactory((_, _) =>
        {
            workerFactoryCalls++;
            return new ValueTask<DbConnection>(new RecordingConnection());
        });
        var writer = new TinyPostgreSqlAdoNetOutboxWriter(options, new RecordingServiceProvider());

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Equal(0, workerFactoryCalls);
    }

    [Fact]
    public async Task Add_async_does_not_open_connection()
    {
        var connection = new RecordingConnection();
        var writer = NewWriter(connection, new RecordingTransaction(connection));

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Equal(0, connection.OpenCount);
    }

    [Fact]
    public async Task Add_async_does_not_begin_transaction()
    {
        var connection = new RecordingConnection();
        var writer = NewWriter(connection, new RecordingTransaction(connection));

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Equal(0, connection.BeginTransactionCount);
    }

    [Fact]
    public async Task Add_async_does_not_commit_transaction()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var writer = NewWriter(connection, transaction);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.False(transaction.WasCommitted);
    }

    [Fact]
    public async Task Add_async_does_not_rollback_transaction()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var writer = NewWriter(connection, transaction);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.False(transaction.WasRolledBack);
    }

    [Fact]
    public async Task Add_async_does_not_dispose_context_connection()
    {
        var connection = new RecordingConnection();
        var writer = NewWriter(connection, new RecordingTransaction(connection));

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.False(connection.WasDisposed);
    }

    [Fact]
    public async Task Add_async_does_not_dispose_context_transaction()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var writer = NewWriter(connection, transaction);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.False(transaction.WasDisposed);
    }

    [Fact]
    public void Insert_sql_rejects_null_table_name()
    {
        Assert.Throws<ArgumentNullException>(() => TinyPostgreSqlAdoNetSql.Insert(null!));
    }

    [Fact]
    public void Insert_sql_uses_quoted_postgre_sql_identifiers()
    {
        var sql = TinyPostgreSqlAdoNetSql.Insert(TinyPostgreSqlAdoNetTableName.Parse("public.TinyOutbox"));

        Assert.Contains("INSERT INTO \"public\".\"TinyOutbox\"", sql);
        Assert.Contains("\"EventType\"", sql);
        Assert.Contains("@Payload", sql);
    }

    private static TinyPostgreSqlAdoNetOutboxWriter NewWriter(
        DbConnection connection,
        DbTransaction transaction)
    {
        return new TinyPostgreSqlAdoNetOutboxWriter(
            NewOptions(connection, transaction),
            new RecordingServiceProvider());
    }

    private static TinyEventsPostgreSqlAdoNetOptions NewOptions(
        DbConnection connection,
        DbTransaction transaction)
    {
        var options = new TinyEventsPostgreSqlAdoNetOptions();
        options.UseCurrentTransaction(_ => new TinyPostgreSqlAdoNetTransactionContext(connection, transaction));
        return options;
    }

    private static TinyOutboxMessage NewMessage()
    {
        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "UserCreated",
            Payload = "{}",
            Status = TinyOutboxMessageStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
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
        private readonly RecordingParameterCollection parameters = new RecordingParameterCollection();
        private ConnectionState state = ConnectionState.Open;

        public RecordingCommand LastCommand { get; private set; }

        public bool WasDisposed { get; private set; }

        public int OpenCount { get; private set; }

        public int BeginTransactionCount { get; private set; }

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

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            BeginTransactionCount++;
            return new RecordingTransaction(this);
        }

        protected override DbCommand CreateDbCommand()
        {
            LastCommand = new RecordingCommand(this);
            return LastCommand;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
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

        public bool WasCommitted { get; private set; }

        public bool WasRolledBack { get; private set; }

        public bool WasDisposed { get; private set; }

        public override void Commit()
        {
            WasCommitted = true;
        }

        public override void Rollback()
        {
            WasRolledBack = true;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class RecordingCommand : DbCommand
    {
        private readonly RecordingParameterCollection parameters = new RecordingParameterCollection();
        private readonly DbConnection connection;

        public RecordingCommand(DbConnection connection)
        {
            this.connection = connection;
        }

        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection
        {
            get => connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection => parameters;

        protected override DbTransaction DbTransaction { get; set; }

        public new DbTransaction Transaction
        {
            get => DbTransaction;
            set => DbTransaction = value;
        }

        public new RecordingParameterCollection Parameters => parameters;

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            return 1;
        }

        public override object ExecuteScalar()
        {
            return null;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new RecordingParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override string SourceColumn { get; set; } = string.Empty;
        public override object Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> parameters = new List<DbParameter>();

        public override int Count => parameters.Count;
        public override object SyncRoot => this;
        public override int Add(object value)
        {
            parameters.Add((DbParameter)value);
            return parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => parameters.Clear();
        public override bool Contains(object value) => parameters.Contains((DbParameter)value);
        public override bool Contains(string value) => parameters.Any(parameter => parameter.ParameterName == value);
        public override void CopyTo(Array array, int index) => parameters.ToArray().CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => parameters.GetEnumerator();
        public override int IndexOf(object value) => parameters.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => parameters.FindIndex(parameter => parameter.ParameterName == parameterName);
        public override void Insert(int index, object value) => parameters.Insert(index, (DbParameter)value);
        public override void Remove(object value) => parameters.Remove((DbParameter)value);
        public override void RemoveAt(int index) => parameters.RemoveAt(index);
        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);

            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index) => parameters[index];
        protected override DbParameter GetParameter(string parameterName) => parameters[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => parameters[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => parameters[IndexOf(parameterName)] = value;
    }
#nullable restore
}
