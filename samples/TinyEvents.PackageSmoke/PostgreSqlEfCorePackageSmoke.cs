using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinyEvents.PostgreSql.EntityFrameworkCore;
using TinyEvents.Worker;

namespace TinyEvents.PackageSmoke;

public static class PostgreSqlEfCorePackageSmoke
{
    public static async ValueTask RunAsync(string connectionString)
    {
        await PostgreSqlPackageSmokeDatabase.ResetOutboxAsync(connectionString);

        var services = new ServiceCollection();

        services.AddDbContext<PostgreSqlSmokeDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddSingleton<SmokeLog>();
        services.UsePostgreSqlEntityFrameworkCoreOutbox<PostgreSqlSmokeDbContext>();
        services.AddTinyEventsWorker(options =>
        {
            options.WorkerId = "package-smoke-postgresql-ef";
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
        var dbContext = scope.ServiceProvider.GetRequiredService<PostgreSqlSmokeDbContext>();

        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxWriter>());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>());
        PackageSmokeAssertions.RequireService(provider.GetServices<IHostedService>().Single());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetServices<IEventConsumer<PostgreSqlEfCoreSmokeEvent>>().Single());

        await publisher.PublishAsync(new PostgreSqlEfCoreSmokeEvent(Guid.NewGuid()));
        await dbContext.SaveChangesAsync();
        await processor.ProcessPendingAsync();

        var log = provider.GetRequiredService<SmokeLog>();
        PackageSmokeAssertions.RequireCondition(log.PostgreSqlEfCoreCount == 1);
    }
}

public sealed class PostgreSqlSmokeDbContext : DbContext
{
    public PostgreSqlSmokeDbContext(DbContextOptions<PostgreSqlSmokeDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseTinyEventsOutbox();
    }
}

public sealed class PostgreSqlEfCoreSmokeConsumer : IEventConsumer<PostgreSqlEfCoreSmokeEvent>
{
    private readonly SmokeLog log;

    public PostgreSqlEfCoreSmokeConsumer(SmokeLog log)
    {
        this.log = log;
    }

    public ValueTask ConsumeAsync(
        PostgreSqlEfCoreSmokeEvent @event,
        CancellationToken cancellationToken)
    {
        log.RecordPostgreSqlEfCore();
        return ValueTask.CompletedTask;
    }
}

public sealed record PostgreSqlEfCoreSmokeEvent(Guid Id);
