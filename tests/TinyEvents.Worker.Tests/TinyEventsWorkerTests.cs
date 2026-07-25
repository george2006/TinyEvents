using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        services.UseTinyEvents(options =>
        {
            options.MaxAttempts = 9;
            options.RetryDelay = TimeSpan.FromSeconds(13);
        });

        using var provider = services.BuildServiceProvider();
        var coreOptions = provider.GetRequiredService<TinyEventsOptions>();

        Assert.Equal("worker-before", coreOptions.WorkerId);
        Assert.Equal(3, coreOptions.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(11), coreOptions.ClaimTimeout);
        Assert.Equal(9, coreOptions.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(13), coreOptions.RetryDelay);
    }

    [Fact]
    public void Worker_options_apply_when_worker_registered_after_core()
    {
        var services = new ServiceCollection();

        services.UseTinyEvents(options =>
        {
            options.MaxAttempts = 9;
            options.RetryDelay = TimeSpan.FromSeconds(13);
        });
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
        Assert.Equal(9, coreOptions.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(13), coreOptions.RetryDelay);
    }

    [Fact]
    public void Add_tiny_events_worker_preserves_core_retry_configuration()
    {
        var services = new ServiceCollection();

        services.UseTinyEvents(options =>
        {
            options.MaxAttempts = 11;
            options.RetryDelay = TimeSpan.FromSeconds(17);
        });
        services.AddTinyEventsWorker(options =>
        {
            options.WorkerId = "worker-1";
            options.BatchSize = 12;
            options.ClaimTimeout = TimeSpan.FromSeconds(45);
        });

        using var provider = services.BuildServiceProvider();
        var coreOptions = provider.GetRequiredService<TinyEventsOptions>();

        Assert.Equal(11, coreOptions.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(17), coreOptions.RetryDelay);
    }

    [Fact]
    public void Add_tiny_events_worker_rejects_empty_configured_worker_id()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(
            () => services.AddTinyEventsWorker(options => options.WorkerId = " "));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_tiny_events_worker_rejects_non_positive_batch_size(int batchSize)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddTinyEventsWorker(options => options.BatchSize = batchSize));
    }

    [Fact]
    public void Add_tiny_events_worker_rejects_negative_polling_interval()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddTinyEventsWorker(options => options.PollingInterval = TimeSpan.FromMilliseconds(-1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_tiny_events_worker_rejects_non_positive_claim_timeout(int milliseconds)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddTinyEventsWorker(options => options.ClaimTimeout = TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void Add_tiny_events_worker_allows_zero_polling_interval()
    {
        var services = new ServiceCollection();

        services.AddTinyEventsWorker(options => options.PollingInterval = TimeSpan.Zero);

        using var provider = services.BuildServiceProvider();
        var workerOptions = provider.GetRequiredService<TinyEventsWorkerOptions>();

        Assert.Equal(TimeSpan.Zero, workerOptions.PollingInterval);
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

    [Fact]
    public async Task Background_service_continues_after_processing_iteration_fails()
    {
        FailingThenRecordingProcessor.Reset();
        var logger = new RecordingLogger<TinyEventsBackgroundService>();
        var services = new ServiceCollection();
        services.AddSingleton<ITinyOutboxProcessor, FailingThenRecordingProcessor>();
        services.AddSingleton(new TinyEventsWorkerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(1)
        });
        services.AddSingleton<ILogger<TinyEventsBackgroundService>>(logger);
        services.AddSingleton<TinyEventsBackgroundService>();
        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<TinyEventsBackgroundService>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(cancellation.Token);
        await FailingThenRecordingProcessor.SecondCall.Task.WaitAsync(cancellation.Token);
        await worker.StopAsync(CancellationToken.None).WaitAsync(cancellation.Token);

        Assert.True(FailingThenRecordingProcessor.CallCount >= 2);
        Assert.Collection(
            logger.Entries,
            failure =>
            {
                Assert.Equal(LogLevel.Warning, failure.LogLevel);
                Assert.Equal(1100, failure.EventId.Id);
                Assert.Equal("WorkerIterationFailed", failure.EventId.Name);
                Assert.Equal(1, failure.Properties["ConsecutiveFailures"]);
                Assert.IsType<InvalidOperationException>(failure.Exception);
            },
            recovery =>
            {
                Assert.Equal(LogLevel.Information, recovery.LogLevel);
                Assert.Equal(1101, recovery.EventId.Id);
                Assert.Equal("WorkerRecovered", recovery.EventId.Name);
                Assert.Equal(1, recovery.Properties["ConsecutiveFailures"]);
                Assert.Null(recovery.Exception);
            });
    }

    [Fact]
    public async Task Background_service_stops_active_iteration_without_logging_cancellation_as_error()
    {
        var processor = new CancellationAwareProcessor();
        var logger = new RecordingLogger<TinyEventsBackgroundService>();
        var services = new ServiceCollection();
        services.AddSingleton<ITinyOutboxProcessor>(processor);
        services.AddSingleton(new TinyEventsWorkerOptions());
        services.AddSingleton<ILogger<TinyEventsBackgroundService>>(logger);
        services.AddSingleton<TinyEventsBackgroundService>();
        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<TinyEventsBackgroundService>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(CancellationToken.None);
        await processor.Started.Task.WaitAsync(timeout.Token);
        await worker.StopAsync(timeout.Token);

        Assert.True(processor.CancellationObserved);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Background_service_throttles_repeated_failures_and_reports_recovery()
    {
        FailingThenRecordingProcessor.Reset(failuresBeforeSuccess: 10);
        var logger = new RecordingLogger<TinyEventsBackgroundService>();
        var services = new ServiceCollection();
        services.AddSingleton<ITinyOutboxProcessor, FailingThenRecordingProcessor>();
        services.AddSingleton(new TinyEventsWorkerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(1)
        });
        services.AddSingleton<ILogger<TinyEventsBackgroundService>>(logger);
        services.AddSingleton<TinyEventsBackgroundService>();
        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<TinyEventsBackgroundService>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(CancellationToken.None);
        await WaitForLogAsync(logger, eventId: 1101, timeout.Token);
        await worker.StopAsync(timeout.Token);

        Assert.Equal(4, logger.Entries.Count(entry => entry.EventId.Id == 1100));
        Assert.Equal(2, logger.Entries.Count(entry => entry.EventId.Id == 1104));
        Assert.Single(logger.Entries, entry => entry.EventId.Id == 1101);
        Assert.All(
            logger.Entries.Where(entry => entry.EventId.Id == 1100),
            entry => Assert.Equal(LogLevel.Warning, entry.LogLevel));
        Assert.All(
            logger.Entries.Where(entry => entry.EventId.Id == 1104),
            entry => Assert.Equal(LogLevel.Error, entry.LogLevel));
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1104
                && Equals(entry.Properties["ConsecutiveFailures"], 5));
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == 1104
                && Equals(entry.Properties["ConsecutiveFailures"], 10));
    }

    private static async Task WaitForLogAsync<T>(
        RecordingLogger<T> logger,
        int eventId,
        CancellationToken cancellationToken)
    {
        while (!logger.Entries.Any(entry => entry.EventId.Id == eventId))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        }
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

    private sealed class FailingThenRecordingProcessor : ITinyOutboxProcessor
    {
        private static int failuresBeforeSuccess = 1;

        public static int CallCount { get; private set; }

        public static TaskCompletionSource SecondCall { get; private set; } = NewTaskCompletionSource();

        public static void Reset(int failuresBeforeSuccess = 1)
        {
            CallCount = 0;
            FailingThenRecordingProcessor.failuresBeforeSuccess = failuresBeforeSuccess;
            SecondCall = NewTaskCompletionSource();
        }

        public ValueTask ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (CallCount <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException("database failed");
            }

            SecondCall.TrySetResult();
            return ValueTask.CompletedTask;
        }

        private static TaskCompletionSource NewTaskCompletionSource()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class CancellationAwareProcessor : ITinyOutboxProcessor
    {
        public TaskCompletionSource Started { get; } = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved { get; private set; }

        public async ValueTask ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new ConcurrentQueue<LogEntry>();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Enqueue(new LogEntry(
                eventId,
                logLevel,
                formatter(state, exception),
                exception,
                GetProperties(state)));
        }

        private static IReadOnlyDictionary<string, object?> GetProperties<TState>(TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> properties)
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            return properties.ToDictionary(
                property => property.Key,
                property => property.Value,
                StringComparer.Ordinal);
        }
    }

    private sealed record LogEntry(
        EventId EventId,
        LogLevel LogLevel,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}
