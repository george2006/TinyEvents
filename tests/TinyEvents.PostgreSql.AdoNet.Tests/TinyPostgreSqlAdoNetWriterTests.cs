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

    [Fact]
    public void Claim_sql_rejects_null_table_name()
    {
        Assert.Throws<ArgumentNullException>(() => TinyPostgreSqlAdoNetSql.ClaimPending(null!));
    }

    [Fact]
    public void Claim_sql_uses_atomic_update_with_postgre_sql_skip_locked()
    {
        var sql = TinyPostgreSqlAdoNetSql.ClaimPending(TinyPostgreSqlAdoNetTableName.Parse("public.TinyOutbox"));

        Assert.Contains("WITH claimed AS", sql);
        Assert.Contains("FOR UPDATE SKIP LOCKED", sql);
        Assert.Contains("UPDATE \"public\".\"TinyOutbox\" AS outbox", sql);
        Assert.Contains("FROM claimed", sql);
        Assert.Contains("RETURNING", sql);
        Assert.Contains("@WorkerId", sql);
        Assert.Contains("@ClaimExpiresAtUtc", sql);
    }

    [Fact]
    public void Claim_sql_reclaims_expired_processing_messages()
    {
        var sql = TinyPostgreSqlAdoNetSql.ClaimPending(TinyPostgreSqlAdoNetTableName.Parse("TinyOutbox"));

        Assert.Contains("\"Status\" = @ProcessingStatus", sql);
        Assert.Contains("\"ClaimExpiresAtUtc\" <= @Now", sql);
    }

    [Fact]
    public void Claim_sql_does_not_claim_future_retry_messages()
    {
        var sql = TinyPostgreSqlAdoNetSql.ClaimPending(TinyPostgreSqlAdoNetTableName.Parse("TinyOutbox"));

        Assert.Contains("\"NextAttemptAtUtc\" IS NULL OR \"NextAttemptAtUtc\" <= @Now", sql);
    }

    [Fact]
    public void Mark_processed_sql_rejects_null_table_name()
    {
        Assert.Throws<ArgumentNullException>(() => TinyPostgreSqlAdoNetSql.MarkProcessed(null!));
    }

    [Fact]
    public void Mark_processed_sql_limits_update_to_current_worker()
    {
        var sql = TinyPostgreSqlAdoNetSql.MarkProcessed(TinyPostgreSqlAdoNetTableName.Parse("TinyOutbox"));

        Assert.Contains("\"ClaimedBy\" = @WorkerId", sql);
        Assert.Contains("\"Status\" = @ProcessingStatus", sql);
        Assert.Contains("@ProcessedAtUtc", sql);
    }

    [Fact]
    public void Mark_failed_sql_rejects_null_table_name()
    {
        Assert.Throws<ArgumentNullException>(() => TinyPostgreSqlAdoNetSql.MarkFailed(null!));
    }

    [Fact]
    public void Mark_failed_sql_limits_update_to_current_worker()
    {
        var sql = TinyPostgreSqlAdoNetSql.MarkFailed(TinyPostgreSqlAdoNetTableName.Parse("TinyOutbox"));

        Assert.Contains("\"ClaimedBy\" = @WorkerId", sql);
        Assert.Contains("\"Status\" = @ProcessingStatus", sql);
        Assert.Contains("@NextAttemptAtUtc", sql);
        Assert.Contains("@LastError", sql);
    }

    [Fact]
    public void Store_rejects_null_options()
    {
        var factory = new RecordingWorkerConnectionFactory(new RecordingConnection());

        Assert.Throws<ArgumentNullException>(() => new TinyPostgreSqlAdoNetOutboxStore(null!, factory));
    }

    [Fact]
    public void Store_rejects_null_connection_factory()
    {
        var options = new TinyEventsPostgreSqlAdoNetOptions();

        Assert.Throws<ArgumentNullException>(() => new TinyPostgreSqlAdoNetOutboxStore(options, null!));
    }

    [Fact]
    public async Task Claim_pending_uses_worker_connection_factory()
    {
        var factory = new RecordingWorkerConnectionFactory(new RecordingConnection());
        var store = NewStore(factory);

        await store.ClaimPendingAsync(1, "worker", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task Mark_processed_uses_worker_connection_factory()
    {
        var factory = new RecordingWorkerConnectionFactory(new RecordingConnection());
        var store = NewStore(factory);

        await store.MarkProcessedAsync(Guid.NewGuid(), "worker", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task Mark_failed_uses_worker_connection_factory()
    {
        var factory = new RecordingWorkerConnectionFactory(new RecordingConnection());
        var store = NewStore(factory);

        await store.MarkFailedAsync(Guid.NewGuid(), "worker", "boom", 1, null, CancellationToken.None);

        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task Worker_connection_is_disposed_after_worker_operation_if_owned_by_factory()
    {
        var connection = new RecordingConnection();
        var store = NewStore(new RecordingWorkerConnectionFactory(connection));

        await store.MarkProcessedAsync(Guid.NewGuid(), "worker", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(connection.WasDisposed);
    }

    [Fact]
    public async Task Claim_pending_rejects_null_worker_id()
    {
        var store = NewStore(new RecordingWorkerConnectionFactory(new RecordingConnection()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.ClaimPendingAsync(1, null!, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task Mark_processed_rejects_null_worker_id()
    {
        var store = NewStore(new RecordingWorkerConnectionFactory(new RecordingConnection()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.MarkProcessedAsync(Guid.NewGuid(), null!, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task Mark_failed_rejects_null_worker_id()
    {
        var store = NewStore(new RecordingWorkerConnectionFactory(new RecordingConnection()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.MarkFailedAsync(Guid.NewGuid(), null!, "boom", 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task Mark_failed_rejects_null_error()
    {
        var store = NewStore(new RecordingWorkerConnectionFactory(new RecordingConnection()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.MarkFailedAsync(Guid.NewGuid(), "worker", null!, 1, null, CancellationToken.None));
    }

    private static TinyPostgreSqlAdoNetOutboxStore NewStore(
        ITinyPostgreSqlAdoNetWorkerConnectionFactory factory)
    {
        return new TinyPostgreSqlAdoNetOutboxStore(
            new TinyEventsPostgreSqlAdoNetOptions(),
            factory);
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

    private sealed class RecordingWorkerConnectionFactory : ITinyPostgreSqlAdoNetWorkerConnectionFactory
    {
        private readonly RecordingConnection connection;

        public RecordingWorkerConnectionFactory(RecordingConnection connection)
        {
            this.connection = connection;
        }

        public int CallCount { get; private set; }

        public ValueTask<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return new ValueTask<DbConnection>(connection);
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
            return new EmptyDataReader();
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<DbDataReader>(new EmptyDataReader());
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class EmptyDataReader : DbDataReader
    {
        public override int FieldCount => 0;

        public override bool HasRows => false;

        public override bool IsClosed => false;

        public override int RecordsAffected => 0;

        public override int Depth => 0;

        public override object this[int ordinal] => throw new IndexOutOfRangeException();

        public override object this[string name] => throw new IndexOutOfRangeException();

        public override bool Read()
        {
            return false;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public override bool NextResult()
        {
            return false;
        }

        public override object GetValue(int ordinal)
        {
            throw new IndexOutOfRangeException();
        }

        public override int GetValues(object[] values)
        {
            return 0;
        }

        public override string GetName(int ordinal)
        {
            throw new IndexOutOfRangeException();
        }

        public override int GetOrdinal(string name)
        {
            throw new IndexOutOfRangeException();
        }

        public override string GetDataTypeName(int ordinal)
        {
            throw new IndexOutOfRangeException();
        }

        public override Type GetFieldType(int ordinal)
        {
            throw new IndexOutOfRangeException();
        }

        public override bool IsDBNull(int ordinal)
        {
            throw new IndexOutOfRangeException();
        }

        public override System.Collections.IEnumerator GetEnumerator()
        {
            return Array.Empty<object>().GetEnumerator();
        }

        public override bool GetBoolean(int ordinal) => throw new IndexOutOfRangeException();
        public override byte GetByte(int ordinal) => throw new IndexOutOfRangeException();
        public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => throw new IndexOutOfRangeException();
        public override char GetChar(int ordinal) => throw new IndexOutOfRangeException();
        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => throw new IndexOutOfRangeException();
        public override Guid GetGuid(int ordinal) => throw new IndexOutOfRangeException();
        public override short GetInt16(int ordinal) => throw new IndexOutOfRangeException();
        public override int GetInt32(int ordinal) => throw new IndexOutOfRangeException();
        public override long GetInt64(int ordinal) => throw new IndexOutOfRangeException();
        public override float GetFloat(int ordinal) => throw new IndexOutOfRangeException();
        public override double GetDouble(int ordinal) => throw new IndexOutOfRangeException();
        public override string GetString(int ordinal) => throw new IndexOutOfRangeException();
        public override decimal GetDecimal(int ordinal) => throw new IndexOutOfRangeException();
        public override DateTime GetDateTime(int ordinal) => throw new IndexOutOfRangeException();
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
