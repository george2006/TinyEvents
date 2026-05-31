using System.Data;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents.SqlServer.AdoNet;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class TinySqlServerAdoNetProviderTests
{
    [Fact]
    public async Task UseSqlServerAdoNetOutbox_accepts_current_transaction_delegate()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        using var provider = BuildProvider(options =>
        {
            options.UseCurrentTransaction(_ => new TinyAdoNetTransactionContext(connection, transaction));
            options.UseWorkerConnectionFactory((_, _) => new ValueTask<DbConnection>(new RecordingConnection()));
        });
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ITinyOutboxWriter>();

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Same(transaction, connection.LastCommand!.Transaction);
    }

    [Fact]
    public void UseSqlServerAdoNetOutbox_accepts_worker_connection_factory_delegate()
    {
        var services = new ServiceCollection();

        services.UseSqlServerAdoNetOutbox(options =>
        {
            options.UseWorkerConnectionFactory((_, _) => new ValueTask<DbConnection>(new RecordingConnection()));
        });

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ITinySqlServerAdoNetWorkerConnectionFactory)
                && descriptor.ImplementationType == typeof(TinySqlServerAdoNetWorkerConnectionFactory));
    }

    [Fact]
    public void UseSqlServerAdoNetOutbox_does_not_register_unit_of_work()
    {
        var services = new ServiceCollection();

        services.UseSqlServerAdoNetOutbox(options =>
        {
            options.UseWorkerConnectionFactory((_, _) => new ValueTask<DbConnection>(new RecordingConnection()));
        });

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Name.Contains("UnitOfWork", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_fails_clearly_if_current_transaction_delegate_not_configured()
    {
        var writer = new TinySqlServerAdoNetOutboxWriter(new TinyEventsSqlServerAdoNetOptions(), new ServiceCollection().BuildServiceProvider());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.AddAsync(NewMessage(), CancellationToken.None));

        Assert.Contains("Configure UseCurrentTransaction(...)", exception.Message);
    }

    [Fact]
    public async Task PublishAsync_uses_configured_current_transaction_context()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var writer = NewWriter(connection, transaction);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Same(transaction, connection.LastCommand!.Transaction);
        Assert.Contains("INSERT INTO [TinyOutbox]", connection.LastCommand.CommandText!);
        Assert.True(connection.LastCommand.Parameters.Contains("@Payload"));
    }

    [Fact]
    public async Task PublishAsync_throws_when_current_transaction_context_is_missing()
    {
        var options = new TinyEventsSqlServerAdoNetOptions();
        options.UseCurrentTransaction(_ => null);
        var writer = new TinySqlServerAdoNetOutboxWriter(options, new ServiceCollection().BuildServiceProvider());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.AddAsync(NewMessage(), CancellationToken.None));

        Assert.Contains("application-owned DbConnection and DbTransaction", exception.Message);
    }

    [Fact]
    public async Task PublishAsync_does_not_use_worker_connection_factory()
    {
        var workerFactoryCalls = 0;
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var services = new ServiceCollection();
        var options = new TinyEventsSqlServerAdoNetOptions();
        options.UseCurrentTransaction(_ => new TinyAdoNetTransactionContext(connection, transaction));
        options.UseWorkerConnectionFactory((_, _) =>
        {
            workerFactoryCalls++;
            return new ValueTask<DbConnection>(new RecordingConnection());
        });
        services.AddSingleton(options);
        using var provider = services.BuildServiceProvider();
        var writer = new TinySqlServerAdoNetOutboxWriter(options, provider);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Equal(0, workerFactoryCalls);
    }

    [Fact]
    public async Task PublishAsync_does_not_open_connection()
    {
        var connection = new RecordingConnection();
        connection.ResetOpenCount();
        var writer = NewWriter(connection, new RecordingTransaction(connection));

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Equal(0, connection.OpenCount);
    }

    [Fact]
    public async Task PublishAsync_does_not_begin_transaction()
    {
        var connection = new RecordingConnection();
        var writer = NewWriter(connection, new RecordingTransaction(connection));

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.Equal(0, connection.BeginTransactionCount);
    }

    [Fact]
    public async Task PublishAsync_does_not_commit_transaction()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var writer = NewWriter(connection, transaction);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.False(transaction.WasCommitted);
    }

    [Fact]
    public async Task PublishAsync_does_not_rollback_transaction()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var writer = NewWriter(connection, transaction);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.False(transaction.WasRolledBack);
    }

    [Fact]
    public async Task PublishAsync_does_not_dispose_context_connection()
    {
        var connection = new RecordingConnection();
        var writer = NewWriter(connection, new RecordingTransaction(connection));

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.False(connection.WasDisposed);
    }

    [Fact]
    public async Task PublishAsync_does_not_dispose_context_transaction()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        var writer = NewWriter(connection, transaction);

        await writer.AddAsync(NewMessage(), CancellationToken.None);

        Assert.False(transaction.WasDisposed);
    }

    [Fact]
    public async Task ClaimPendingAsync_uses_worker_connection_factory()
    {
        var factory = new RecordingWorkerConnectionFactory(new RecordingConnection());
        var store = NewStore(factory);

        await store.ClaimPendingAsync(1, "worker", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task MarkProcessedAsync_uses_worker_connection_factory()
    {
        var factory = new RecordingWorkerConnectionFactory(new RecordingConnection());
        var store = NewStore(factory);

        await store.MarkProcessedAsync(Guid.NewGuid(), "worker", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task MarkFailedAsync_uses_worker_connection_factory()
    {
        var factory = new RecordingWorkerConnectionFactory(new RecordingConnection());
        var store = NewStore(factory);

        await store.MarkFailedAsync(Guid.NewGuid(), "worker", "boom", 1, null, CancellationToken.None);

        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public void AdoNet_store_does_not_implement_writer()
    {
        Assert.False(typeof(ITinyOutboxWriter).IsAssignableFrom(typeof(TinySqlServerAdoNetOutboxStore)));
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
    public void AdoNet_provider_does_not_expose_unit_of_work_abstraction()
    {
        var exportedNames = typeof(TinySqlServerAdoNetOutboxWriter).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToArray();

        Assert.DoesNotContain(exportedNames, name => name.Contains("UnitOfWork", StringComparison.Ordinal));
        Assert.DoesNotContain(exportedNames, name => name.Contains("Uow", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("TinyOutbox", "[TinyOutbox]")]
    [InlineData("app.TinyOutbox", "[app].[TinyOutbox]")]
    public void Table_name_formats_sql_server_names(string tableName, string expected)
    {
        var parsed = TinySqlServerAdoNetTableName.Parse(tableName);

        Assert.Equal(expected, parsed.ToSqlServerName());
    }

    [Theory]
    [InlineData("TinyOutbox", "dbo.TinyOutbox")]
    [InlineData("app.TinyOutbox", "app.TinyOutbox")]
    public void Table_name_formats_sql_server_object_names(string tableName, string expected)
    {
        var parsed = TinySqlServerAdoNetTableName.Parse(tableName);

        Assert.Equal(expected, parsed.ToSqlServerObjectName());
    }

    [Fact]
    public void Schema_helper_creates_default_outbox_sql()
    {
        var sql = TinySqlServerAdoNetSchema.CreateOutboxSql();

        Assert.Contains("OBJECT_ID(N'dbo.TinyOutbox'", sql);
        Assert.Contains("CREATE TABLE [dbo].[TinyOutbox]", sql);
        Assert.Contains("IX_TinyOutbox_Pending", sql);
        Assert.Contains("IX_TinyOutbox_ExpiredProcessing", sql);
        Assert.Contains("IX_TinyOutbox_ClaimedBy", sql);
    }

    [Fact]
    public void Schema_helper_creates_custom_table_outbox_sql()
    {
        var sql = TinySqlServerAdoNetSchema.CreateOutboxSql("app.MyOutbox");

        Assert.Contains("OBJECT_ID(N'app.MyOutbox'", sql);
        Assert.Contains("CREATE TABLE [app].[MyOutbox]", sql);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("TinyOutbox;DROP TABLE Users")]
    [InlineData("dbo..TinyOutbox")]
    [InlineData("dbo.Tiny-Outbox")]
    public void Table_name_rejects_unsafe_names(string tableName)
    {
        Assert.Throws<ArgumentException>(() => TinySqlServerAdoNetTableName.Parse(tableName));
    }

    [Fact]
    public void Claim_sql_uses_atomic_update_with_sql_server_locking_hints()
    {
        var sql = TinySqlServerAdoNetSql.ClaimPending(TinySqlServerAdoNetTableName.Parse("dbo.TinyOutbox"));

        Assert.Contains("WITH (UPDLOCK, READPAST, ROWLOCK)", sql);
        Assert.Contains("UPDATE cte", sql);
        Assert.Contains("OUTPUT", sql);
        Assert.Contains("@WorkerId", sql);
        Assert.Contains("@ClaimExpiresAtUtc", sql);
    }

    [Fact]
    public void Claim_sql_reclaims_expired_processing_messages()
    {
        var sql = TinySqlServerAdoNetSql.ClaimPending(TinySqlServerAdoNetTableName.Parse("TinyOutbox"));

        Assert.Contains("Status = @ProcessingStatus", sql);
        Assert.Contains("ClaimExpiresAtUtc <= @Now", sql);
    }

    [Fact]
    public void Claim_sql_does_not_claim_future_retry_messages()
    {
        var sql = TinySqlServerAdoNetSql.ClaimPending(TinySqlServerAdoNetTableName.Parse("TinyOutbox"));

        Assert.Contains("NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now", sql);
    }

    [Fact]
    public void Mark_processed_sql_limits_update_to_current_worker()
    {
        var sql = TinySqlServerAdoNetSql.MarkProcessed(TinySqlServerAdoNetTableName.Parse("TinyOutbox"));

        Assert.Contains("ClaimedBy = @WorkerId", sql);
        Assert.Contains("Status = @ProcessingStatus", sql);
    }

    [Fact]
    public void Mark_failed_sql_limits_update_to_current_worker()
    {
        var sql = TinySqlServerAdoNetSql.MarkFailed(TinySqlServerAdoNetTableName.Parse("TinyOutbox"));

        Assert.Contains("ClaimedBy = @WorkerId", sql);
        Assert.Contains("Status = @ProcessingStatus", sql);
        Assert.Contains("@NextAttemptAtUtc", sql);
    }

    private static ServiceProvider BuildProvider(Action<TinyEventsSqlServerAdoNetOptions> configure)
    {
        var services = new ServiceCollection();
        services.UseSqlServerAdoNetOutbox(configure);
        return services.BuildServiceProvider();
    }

    private static TinySqlServerAdoNetOutboxWriter NewWriter(
        DbConnection connection,
        DbTransaction transaction)
    {
        var options = new TinyEventsSqlServerAdoNetOptions();
        options.UseCurrentTransaction(_ => new TinyAdoNetTransactionContext(connection, transaction));
        return new TinySqlServerAdoNetOutboxWriter(options, new ServiceCollection().BuildServiceProvider());
    }

    private static TinySqlServerAdoNetOutboxStore NewStore(ITinySqlServerAdoNetWorkerConnectionFactory factory)
    {
        return new TinySqlServerAdoNetOutboxStore(new TinyEventsSqlServerAdoNetOptions(), factory);
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
    private sealed class RecordingWorkerConnectionFactory : ITinySqlServerAdoNetWorkerConnectionFactory
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
            connection.Open();
            return new ValueTask<DbConnection>(connection);
        }
    }

    private sealed class RecordingConnection : DbConnection
    {
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

        public void ResetOpenCount()
        {
            OpenCount = 0;
        }

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
