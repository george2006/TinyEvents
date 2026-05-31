using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinyEvents.SqlServer.EntityFrameworkCore;
using TinyEvents.Worker;

namespace TinyEvents.PackageSmoke;

public static class EfCorePackageSmoke
{
    public static async ValueTask RunAsync(string connectionString)
    {
        await PackageSmokeDatabase.ResetOutboxAsync(connectionString);

        var services = new ServiceCollection();

        services.AddDbContext<SmokeDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });

        services.AddSingleton<SmokeLog>();
        services.UseSqlServerEntityFrameworkCoreOutbox<SmokeDbContext>();
        services.AddTinyEventsWorker(options =>
        {
            options.WorkerId = "package-smoke-ef";
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
        var dbContext = scope.ServiceProvider.GetRequiredService<SmokeDbContext>();

        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxWriter>());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetRequiredService<ITinyOutboxStore>());
        PackageSmokeAssertions.RequireService(provider.GetServices<IHostedService>().Single());
        PackageSmokeAssertions.RequireService(scope.ServiceProvider.GetServices<IEventConsumer<EfCoreSmokeEvent>>().Single());

        await publisher.PublishAsync(new EfCoreSmokeEvent(Guid.NewGuid()));
        await dbContext.SaveChangesAsync();
        await processor.ProcessPendingAsync();

        var log = provider.GetRequiredService<SmokeLog>();
        PackageSmokeAssertions.RequireCondition(log.EfCoreCount == 1);
    }
}

public sealed class SmokeDbContext : DbContext
{
    public SmokeDbContext(DbContextOptions<SmokeDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseTinyEventsOutbox();
    }
}

public sealed class EfCoreSmokeConsumer : IEventConsumer<EfCoreSmokeEvent>
{
    private readonly SmokeLog log;

    public EfCoreSmokeConsumer(SmokeLog log)
    {
        this.log = log;
    }

    public ValueTask ConsumeAsync(
        EfCoreSmokeEvent @event,
        CancellationToken cancellationToken)
    {
        log.RecordEfCore();
        return ValueTask.CompletedTask;
    }
}

public sealed record EfCoreSmokeEvent(Guid Id);
