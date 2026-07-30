using Npgsql;
using TinyEvents.Migrations.PostgreSql;
using Xunit;

namespace TinyEvents.PostgreSql.Tests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class PostgreSqlMigrationLockTests
{
    private readonly PostgreSqlFixture fixture;

    public PostgreSqlMigrationLockTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Competing_migration_locks_serialize_until_explicit_release()
    {
        var firstLock = MigrationLock();
        var secondLock = MigrationLock();
        await using var firstConnection = await OpenConnectionAsync();
        await using var secondConnection = await OpenConnectionAsync();
        await firstLock.AcquireAsync(firstConnection, CancellationToken.None);

        var secondAcquisition =
            secondLock.AcquireAsync(secondConnection, CancellationToken.None);
        var firstCompletion = await Task.WhenAny(
            secondAcquisition,
            Task.Delay(TimeSpan.FromMilliseconds(250)));
        Assert.NotSame(secondAcquisition, firstCompletion);

        await firstLock.ReleaseAsync(firstConnection, CancellationToken.None);
        await secondAcquisition;
        await secondLock.ReleaseAsync(secondConnection, CancellationToken.None);
    }

    [PostgreSqlIntegrationFact]
    public async Task Acquisition_timeout_throws_a_distinct_timeout_exception()
    {
        var owningLock = MigrationLock();
        var waitingLock = MigrationLock(TimeSpan.FromMilliseconds(150));
        await using var owningConnection = await OpenConnectionAsync();
        await using var waitingConnection = await OpenConnectionAsync();
        await owningLock.AcquireAsync(owningConnection, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => waitingLock.AcquireAsync(
                waitingConnection,
                CancellationToken.None));

        Assert.Contains(
            "waiting for the TinyEvents migration lock for " +
            "'public.TinyOutboxMigrations'",
            exception.Message);
        await owningLock.ReleaseAsync(owningConnection, CancellationToken.None);
    }

    [PostgreSqlIntegrationFact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        var owningLock = MigrationLock();
        var waitingLock = MigrationLock(TimeSpan.FromSeconds(10));
        await using var owningConnection = await OpenConnectionAsync();
        await using var waitingConnection = await OpenConnectionAsync();
        await owningLock.AcquireAsync(owningConnection, CancellationToken.None);
        using var cancellation =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waitingLock.AcquireAsync(
                waitingConnection,
                cancellation.Token));

        await owningLock.ReleaseAsync(owningConnection, CancellationToken.None);
    }

    [PostgreSqlIntegrationFact]
    public async Task Release_from_a_non_owning_session_fails()
    {
        var migrationLock = MigrationLock();
        await using var owningConnection = await OpenConnectionAsync();
        await using var otherConnection = await OpenConnectionAsync();
        await migrationLock.AcquireAsync(owningConnection, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => migrationLock.ReleaseAsync(
                otherConnection,
                CancellationToken.None));

        Assert.Contains("does not hold it", exception.Message);
        await migrationLock.ReleaseAsync(owningConnection, CancellationToken.None);
    }

    [PostgreSqlIntegrationFact]
    public async Task Different_history_tables_use_independent_locks()
    {
        var firstLock = new PostgreSqlMigrationLock(
            PostgreSqlMigrationTableIdentity.Parse("FirstOutbox"));
        var secondLock = new PostgreSqlMigrationLock(
            PostgreSqlMigrationTableIdentity.Parse("SecondOutbox"),
            TimeSpan.FromMilliseconds(150));
        await using var firstConnection = await OpenConnectionAsync();
        await using var secondConnection = await OpenConnectionAsync();
        await firstLock.AcquireAsync(firstConnection, CancellationToken.None);

        await secondLock.AcquireAsync(secondConnection, CancellationToken.None);

        await secondLock.ReleaseAsync(secondConnection, CancellationToken.None);
        await firstLock.ReleaseAsync(firstConnection, CancellationToken.None);
    }

    private static PostgreSqlMigrationLock MigrationLock()
    {
        return new PostgreSqlMigrationLock(
            PostgreSqlMigrationTableIdentity.Parse("TinyOutbox"));
    }

    private static PostgreSqlMigrationLock MigrationLock(TimeSpan timeout)
    {
        return new PostgreSqlMigrationLock(
            PostgreSqlMigrationTableIdentity.Parse("TinyOutbox"),
            timeout);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
