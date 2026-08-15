using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents.Migrations.SqlServer;
using TinyEvents.SqlServer.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.SqlServer.EntityFrameworkCore.Tests;

public sealed class SqlServerEfCoreMigrationConnectionFactoryTests
{
    [Fact]
    public void Resolving_migrator_rejects_a_non_SQL_Server_DbContext_without_opening_it()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(
            options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.UseSqlServerEntityFrameworkCoreOutbox<TestDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<SqlServerTinyEventsMigrator>());

        Assert.Contains("requires the SQL Server EF Core provider", exception.Message);
        Assert.Contains("UseSqlServer(...)", exception.Message);
    }

    [Fact]
    public void Resolving_migrator_with_SQL_Server_does_not_open_the_connection()
    {
        var connection = new RecordingConnection();
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(
            options => options.UseSqlServer(connection));
        services.UseSqlServerEntityFrameworkCoreOutbox<TestDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        _ = scope.ServiceProvider.GetRequiredService<SqlServerTinyEventsMigrator>();

        Assert.Equal(0, connection.OpenCount);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task Lease_closes_a_connection_opened_for_migration_without_disposing_it()
    {
        var connection = new RecordingConnection();
        await using var dbContext = NewSqlServerDbContext(connection);
        var factory =
            new SqlServerEfCoreMigrationConnectionFactory<TestDbContext>(dbContext);

        await using (var lease =
            await factory.CreateOpenConnectionAsync(CancellationToken.None))
        {
            Assert.Same(connection, lease.Connection);
            Assert.Equal(ConnectionState.Open, connection.State);
        }

        Assert.Equal(1, connection.OpenCount);
        Assert.Equal(1, connection.CloseCount);
        Assert.False(connection.WasDisposed);
    }

    [Fact]
    public async Task Lease_preserves_a_pre_opened_DbContext_connection()
    {
        var connection = new RecordingConnection();
        connection.Open();
        await using var dbContext = NewSqlServerDbContext(connection);
        var factory =
            new SqlServerEfCoreMigrationConnectionFactory<TestDbContext>(dbContext);

        await using (var lease =
            await factory.CreateOpenConnectionAsync(CancellationToken.None))
        {
            Assert.Same(connection, lease.Connection);
        }

        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(1, connection.OpenCount);
        Assert.Equal(0, connection.CloseCount);
        Assert.False(connection.WasDisposed);
    }

    private static TestDbContext NewSqlServerDbContext(DbConnection connection)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(connection)
            .Options;

        return new TestDbContext(options);
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
            : base(options)
        {
        }
    }

    private sealed class RecordingConnection : DbConnection
    {
        private ConnectionState state = ConnectionState.Closed;

        public int OpenCount { get; private set; }

        public int CloseCount { get; private set; }

        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "TinyEvents";

        public override string DataSource => "Recording";

        public override string ServerVersion => "1";

        public override ConnectionState State => state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            CloseCount++;
            state = ConnectionState.Closed;
        }

        public override void Open()
        {
            OpenCount++;
            state = ConnectionState.Open;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
