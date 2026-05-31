using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinyEvents.Worker;
using Xunit;

namespace TinyEvents.Worker.Tests;

public sealed class TinyEventsWorkerTests
{
    [Fact]
    public void Add_tiny_events_worker_rejects_null_services()
    {
        Assert.Throws<ArgumentNullException>(
            () => TinyEventsWorkerServiceCollectionExtensions.AddTinyEventsWorker(null!));
    }

    [Fact]
    public void Add_tiny_events_worker_registers_hosted_service()
    {
        var services = new ServiceCollection();

        services.AddTinyEventsWorker();

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(TinyEventsBackgroundService));
    }

    [Fact]
    public void Add_tiny_events_worker_applies_options_to_core()
    {
        var services = new ServiceCollection();

        services.AddTinyEventsWorker(options =>
        {
            options.WorkerId = "worker-1";
            options.BatchSize = 12;
            options.ClaimTimeout = TimeSpan.FromSeconds(45);
            options.PollingInterval = TimeSpan.FromMilliseconds(10);
        });

        var provider = services.BuildServiceProvider();
        var coreOptions = provider.GetRequiredService<TinyEventsOptions>();
        var workerOptions = provider.GetRequiredService<TinyEventsWorkerOptions>();

        Assert.Equal("worker-1", coreOptions.WorkerId);
        Assert.Equal(12, coreOptions.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(45), coreOptions.ClaimTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(10), workerOptions.PollingInterval);
    }

    [Fact]
    public void Worker_options_apply_when_worker_registered_before_core()
    {
        var services = new ServiceCollection();

        services.AddTinyEventsWorker(options =>
        {
            options.WorkerId = "worker-before";
            options.BatchSize = 3;
            options.ClaimTimeout = TimeSpan.FromSeconds(11);
        });
        services.UseTinyEvents();

        using var provider = services.BuildServiceProvider();
        var coreOptions = provider.GetRequiredService<TinyEventsOptions>();

        Assert.Equal("worker-before", coreOptions.WorkerId);
        Assert.Equal(3, coreOptions.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(11), coreOptions.ClaimTimeout);
    }

    [Fact]
    public void Worker_options_apply_when_worker_registered_after_core()
    {
        var services = new ServiceCollection();

        services.UseTinyEvents();
        services.AddTinyEventsWorker(options =>
        {
            options.WorkerId = "worker-after";
            options.BatchSize = 4;
            options.ClaimTimeout = TimeSpan.FromSeconds(12);
        });

        using var provider = services.BuildServiceProvider();
        var coreOptions = provider.GetRequiredService<TinyEventsOptions>();

        Assert.Equal("worker-after", coreOptions.WorkerId);
        Assert.Equal(4, coreOptions.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(12), coreOptions.ClaimTimeout);
    }

    [Fact]
    public void Add_tiny_events_worker_rejects_empty_configured_worker_id()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(
            () => services.AddTinyEventsWorker(options => options.WorkerId = " "));
    }

    [Fact]
    public void Add_tiny_events_worker_leaves_worker_id_unset_when_not_configured()
    {
        var services = new ServiceCollection();

        services.AddTinyEventsWorker();

        using var provider = services.BuildServiceProvider();
        var coreOptions = provider.GetRequiredService<TinyEventsOptions>();

        Assert.Null(coreOptions.WorkerId);
    }

    [Fact]
    public async Task Background_service_process_once_resolves_processor_from_scope()
    {
        RecordingProcessor.CallCount = 0;
        var services = new ServiceCollection();
        services.AddScoped<ITinyOutboxProcessor, RecordingProcessor>();
        services.AddSingleton(new TinyEventsWorkerOptions());
        services.AddSingleton<TinyEventsBackgroundService>();
        var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<TinyEventsBackgroundService>();

        await worker.ProcessOnceAsync();

        Assert.Equal(1, RecordingProcessor.CallCount);
    }

    [Fact]
    public async Task Background_service_creates_scope_per_processing_iteration()
    {
        ScopedProcessor.InstanceIds.Clear();
        var services = new ServiceCollection();
        services.AddScoped<ITinyOutboxProcessor, ScopedProcessor>();
        services.AddSingleton(new TinyEventsWorkerOptions());
        services.AddSingleton<TinyEventsBackgroundService>();
        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<TinyEventsBackgroundService>();

        await worker.ProcessOnceAsync();
        await worker.ProcessOnceAsync();

        Assert.Equal(2, ScopedProcessor.InstanceIds.Count);
        Assert.NotEqual(ScopedProcessor.InstanceIds[0], ScopedProcessor.InstanceIds[1]);
    }

    private sealed class RecordingProcessor : ITinyOutboxProcessor
    {
        public static int CallCount { get; set; }

        public ValueTask ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScopedProcessor : ITinyOutboxProcessor
    {
        private readonly Guid instanceId = Guid.NewGuid();

        public static List<Guid> InstanceIds { get; } = new List<Guid>();

        public ValueTask ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            InstanceIds.Add(instanceId);
            return ValueTask.CompletedTask;
        }
    }
}
