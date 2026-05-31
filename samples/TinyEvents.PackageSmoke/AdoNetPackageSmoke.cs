using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinyEvents.SqlServer.AdoNet;
using TinyEvents.Worker;

namespace TinyEvents.PackageSmoke;

public static class AdoNetPackageSmoke
{
    public static async ValueTask RunAsync(string connectionString)
    {
        await PackageSmokeDatabase.ResetOutboxAsync(connectionString);

        var services = new ServiceCollection();

        services.AddSingleton<SmokeLog>();
        services.AddScoped(_ => new SmokeAdoNetTransaction(connectionString));
        services.UseSqlServerAdoNetOutbox(options =>
        {
            options.UseCurrentTransaction(provider =>
            {
                var current = provider.GetRequiredService<SmokeAdoNetTransaction>();
                return new TinyAdoNetTransactionContext(current.Connection, current.Transaction);
            });

            options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
            {
                var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                return connection;
            });
        });

        services.AddTinyEventsWorker(options =>
        {
            options.WorkerId = "package-smoke-adonet";
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
        var transaction = scope.ServiceProvider.GetRequiredService<SmokeAdoNetTransaction>();

        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxWriter>());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>());
        PackageSmokeAssertions.RequireService(provider.GetServices<IHostedService>().Single());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetServices<IEventConsumer<AdoNetSmokeEvent>>().Single());

        await publisher.PublishAsync(new AdoNetSmokeEvent(Guid.NewGuid()));
        await transaction.CommitAsync();
        await processor.ProcessPendingAsync();

        var log = provider.GetRequiredService<SmokeLog>();
        PackageSmokeAssertions.RequireCondition(log.AdoNetCount == 1);
    }
}

public sealed class SmokeAdoNetTransaction : IDisposable, IAsyncDisposable
{
    public SmokeAdoNetTransaction(string connectionString)
    {
        Connection = new SqlConnection(connectionString);
        Connection.Open();
        Transaction = Connection.BeginTransaction();
    }

    public SqlConnection Connection { get; }

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

public sealed class AdoNetSmokeConsumer : IEventConsumer<AdoNetSmokeEvent>
{
    private readonly SmokeLog log;

    public AdoNetSmokeConsumer(SmokeLog log)
    {
        this.log = log;
    }

    public ValueTask ConsumeAsync(
        AdoNetSmokeEvent @event,
        CancellationToken cancellationToken)
    {
        log.RecordAdoNet();
        return ValueTask.CompletedTask;
    }
}

public sealed record AdoNetSmokeEvent(Guid Id);
