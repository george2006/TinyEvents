using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TinyEvents.PostgreSql.AdoNet;
using TinyEvents.Worker;

namespace TinyEvents.PackageSmoke;

public static class PostgreSqlAdoNetPackageSmoke
{
    public static async ValueTask RunAsync(string connectionString)
    {
        await PostgreSqlPackageSmokeDatabase.ResetOutboxAsync(connectionString);

        var services = new ServiceCollection();

        services.AddSingleton<SmokeLog>();
        services.AddScoped(_ => new PostgreSqlSmokeAdoNetTransaction(connectionString));
        services.UsePostgreSqlAdoNetOutbox(options =>
        {
            options.UseCurrentTransaction(provider =>
            {
                var current = provider.GetRequiredService<PostgreSqlSmokeAdoNetTransaction>();
                return new TinyPostgreSqlAdoNetTransactionContext(current.Connection, current.Transaction);
            });

            options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
            {
                var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                return connection;
            });
        });

        services.AddTinyEventsWorker(options =>
        {
            options.WorkerId = "package-smoke-postgresql-adonet";
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
        var transaction = scope.ServiceProvider.GetRequiredService<PostgreSqlSmokeAdoNetTransaction>();

        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxWriter>());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>());
        PackageSmokeAssertions.RequireService(provider.GetServices<IHostedService>().Single());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetServices<IEventConsumer<PostgreSqlAdoNetSmokeEvent>>().Single());

        await publisher.PublishAsync(new PostgreSqlAdoNetSmokeEvent(Guid.NewGuid()));
        await transaction.CommitAsync();
        await processor.ProcessPendingAsync();

        var log = provider.GetRequiredService<SmokeLog>();
        PackageSmokeAssertions.RequireCondition(log.PostgreSqlAdoNetCount == 1);
    }
}

public sealed class PostgreSqlSmokeAdoNetTransaction : IDisposable, IAsyncDisposable
{
    public PostgreSqlSmokeAdoNetTransaction(string connectionString)
    {
        Connection = new NpgsqlConnection(connectionString);
        Connection.Open();
        Transaction = Connection.BeginTransaction();
    }

    public NpgsqlConnection Connection { get; }

    public DbTransaction Transaction { get; }

    public async ValueTask CommitAsync()
    {
        await Transaction.CommitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }

    public void Dispose()
    {
        Transaction.Dispose();
        Connection.Dispose();
    }
}

public sealed class PostgreSqlAdoNetSmokeConsumer : IEventConsumer<PostgreSqlAdoNetSmokeEvent>
{
    private readonly SmokeLog log;

    public PostgreSqlAdoNetSmokeConsumer(SmokeLog log)
    {
        this.log = log;
    }

    public ValueTask ConsumeAsync(
        PostgreSqlAdoNetSmokeEvent @event,
        CancellationToken cancellationToken)
    {
        log.RecordPostgreSqlAdoNet();
        return ValueTask.CompletedTask;
    }
}

public sealed record PostgreSqlAdoNetSmokeEvent(Guid Id);
