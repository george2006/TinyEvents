using Microsoft.Data.SqlClient;
using TinyEvents.Migrations.SqlServer;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class SqlServerMigrationLockIntegrationTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public SqlServerMigrationLockIntegrationTests(SqlServerFixture fixture)
    {
        this.fixture = fixture;
    }

    [SqlServerIntegrationFact]
    public async Task Competing_migration_locks_serialize_until_explicit_release()
    {
        var firstLock = MigrationLock();
        var secondLock = MigrationLock();
        await using var firstConnection = await OpenConnectionAsync();
        await using var secondConnection = await OpenConnectionAsync();
        await firstLock.AcquireAsync(firstConnection, CancellationToken.None);

        var secondAcquisition = secondLock.AcquireAsync(
            secondConnection,
            CancellationToken.None);
        var firstCompletion = await Task.WhenAny(
            secondAcquisition,
            Task.Delay(TimeSpan.FromMilliseconds(250)));

        Assert.NotSame(secondAcquisition, firstCompletion);

        await firstLock.ReleaseAsync(firstConnection, CancellationToken.None);
        await secondAcquisition;
        await secondLock.ReleaseAsync(secondConnection, CancellationToken.None);
    }

    [SqlServerIntegrationFact]
    public async Task Acquisition_timeout_throws_a_distinct_timeout_exception()
    {
        var owningLock = MigrationLock();
        var waitingLock = MigrationLock(TimeSpan.FromMilliseconds(150));
        await using var owningConnection = await OpenConnectionAsync();
        await using var waitingConnection = await OpenConnectionAsync();
        await owningLock.AcquireAsync(owningConnection, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => waitingLock.AcquireAsync(waitingConnection, CancellationToken.None));

        Assert.Contains(
            "waiting for the TinyEvents migration lock for 'dbo.TinyOutboxMigrations'",
            exception.Message);
        await owningLock.ReleaseAsync(owningConnection, CancellationToken.None);
    }

    [SqlServerIntegrationFact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        var owningLock = MigrationLock();
        var waitingLock = MigrationLock(TimeSpan.FromSeconds(10));
        await using var owningConnection = await OpenConnectionAsync();
        await using var waitingConnection = await OpenConnectionAsync();
        await owningLock.AcquireAsync(owningConnection, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waitingLock.AcquireAsync(waitingConnection, cancellation.Token));

        await owningLock.ReleaseAsync(owningConnection, CancellationToken.None);
    }

    [SqlServerIntegrationFact]
    public async Task Release_from_a_non_owning_session_fails()
    {
        var migrationLock = MigrationLock();
        await using var owningConnection = await OpenConnectionAsync();
        await using var otherConnection = await OpenConnectionAsync();
        await migrationLock.AcquireAsync(owningConnection, CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => migrationLock.ReleaseAsync(otherConnection, CancellationToken.None));

        Assert.Contains("not currently held", exception.Message);
        await migrationLock.ReleaseAsync(owningConnection, CancellationToken.None);
    }

    [SqlServerIntegrationFact]
    public async Task Different_history_tables_use_independent_locks()
    {
        var firstIdentity = SqlServerMigrationTableIdentity.Parse("dbo.FirstOutbox");
        var secondIdentity = SqlServerMigrationTableIdentity.Parse("dbo.SecondOutbox");
        var firstLock = new SqlServerMigrationLock(firstIdentity);
        var secondLock = new SqlServerMigrationLock(
            secondIdentity,
            TimeSpan.FromMilliseconds(150));
        await using var firstConnection = await OpenConnectionAsync();
        await using var secondConnection = await OpenConnectionAsync();
        await firstLock.AcquireAsync(firstConnection, CancellationToken.None);

        await secondLock.AcquireAsync(secondConnection, CancellationToken.None);

        await secondLock.ReleaseAsync(secondConnection, CancellationToken.None);
        await firstLock.ReleaseAsync(firstConnection, CancellationToken.None);
    }

    private static SqlServerMigrationLock MigrationLock()
    {
        return new SqlServerMigrationLock(
            SqlServerMigrationTableIdentity.Parse("TinyOutbox"));
    }

    private static SqlServerMigrationLock MigrationLock(TimeSpan timeout)
    {
        return new SqlServerMigrationLock(
            SqlServerMigrationTableIdentity.Parse("TinyOutbox"),
            timeout);
    }

    private async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
