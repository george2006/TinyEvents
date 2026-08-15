using TinyEvents.Migrations.SqlServer;
using TinyEvents.Testing;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class SqlServerMigrationLoggingTests
{
    [Fact]
    public async Task Caller_cancellation_is_not_logged_as_a_failure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var logger = new RecordingLogger();
        var migrator = new SqlServerTinyEventsMigrator(
            new ThrowingConnectionFactory(
                new OperationCanceledException(cancellation.Token)),
            "app.Events",
            TimeProvider.System,
            logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => migrator.MigrateAsync(cancellation.Token));

        var started = Assert.Single(logger.Entries);
        Assert.Equal(1400, started.EventId.Id);
        Assert.Equal("sqlserver", started.Properties["Provider"]);
        Assert.Equal("app", started.Properties["Schema"]);
        Assert.Equal("Events", started.Properties["Table"]);
    }

    private sealed class ThrowingConnectionFactory
        : ISqlServerMigrationConnectionFactory
    {
        private readonly Exception exception;

        internal ThrowingConnectionFactory(Exception exception)
        {
            this.exception = exception;
        }

        public ValueTask<SqlServerMigrationConnection>
            CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromException<SqlServerMigrationConnection>(
                exception);
        }
    }
}
