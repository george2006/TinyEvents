using Microsoft.Extensions.DependencyInjection;
using TinyEvents.Worker;
using Xunit;

namespace TinyEvents.Worker.Tests;

public sealed class TinyEventsWorkerStartupValidatorTests
{
    [Fact]
    public void Constructor_rejects_null_scope_factory()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TinyEventsWorkerStartupValidator(null!));
    }

    [Fact]
    public void Validate_configuration_resolves_processor_without_invoking_it()
    {
        RecordingProcessor.Reset();
        var services = new ServiceCollection();
        services.AddScoped<ITinyOutboxProcessor, RecordingProcessor>();
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var validator = new TinyEventsWorkerStartupValidator(scopeFactory);

        validator.ValidateConfiguration();

        Assert.Equal(1, RecordingProcessor.CreatedCount);
        Assert.Equal(0, RecordingProcessor.CallCount);
        Assert.Equal(1, RecordingProcessor.DisposedCount);
    }

    [Fact]
    public void Validate_configuration_rejects_missing_processor()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var validator = CreateValidator(provider);

        var exception = Assert.Throws<InvalidOperationException>(
            validator.ValidateConfiguration);

        Assert.Contains(nameof(ITinyOutboxProcessor), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_configuration_rejects_missing_store()
    {
        var services = new ServiceCollection();
        services.UseTinyEvents();
        using var provider = services.BuildServiceProvider();
        var validator = CreateValidator(provider);

        var exception = Assert.Throws<InvalidOperationException>(
            validator.ValidateConfiguration);

        Assert.Contains(
            typeof(TinyOutboxProcessor).FullName!,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_configuration_rejects_conflicting_dispatchers()
    {
        var services = CreateProcessorServices();
        services.AddSingleton<ITinyEventDispatcher>(
            new TinyEventDispatcher<FirstEvent>("Shared.Event"));
        services.AddSingleton<ITinyEventDispatcher>(
            new TinyEventDispatcher<SecondEvent>("Shared.Event"));
        using var provider = services.BuildServiceProvider();
        var validator = CreateValidator(provider);

        var exception = Assert.Throws<InvalidOperationException>(
            validator.ValidateConfiguration);

        Assert.Contains("Shared.Event", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FirstEvent).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondEvent).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_configuration_does_not_resolve_or_invoke_consumers()
    {
        var consumerCreatedCount = 0;
        var services = CreateProcessorServices();
        services.AddScoped<IEventConsumer<FirstEvent>>(_ =>
        {
            consumerCreatedCount++;
            return new NoOpConsumer();
        });
        services.AddSingleton<ITinyEventDispatcher>(
            new TinyEventDispatcher<FirstEvent>(typeof(FirstEvent).FullName!));
        using var provider = services.BuildServiceProvider();
        var validator = CreateValidator(provider);

        validator.ValidateConfiguration();

        Assert.Equal(0, consumerCreatedCount);
    }

    private static ServiceCollection CreateProcessorServices()
    {
        var services = new ServiceCollection();
        services.UseTinyEvents();
        services.AddSingleton<ITinyOutboxStore, NoOpStore>();
        return services;
    }

    private static TinyEventsWorkerStartupValidator CreateValidator(
        IServiceProvider serviceProvider)
    {
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new TinyEventsWorkerStartupValidator(scopeFactory);
    }

    private sealed class RecordingProcessor : ITinyOutboxProcessor, IDisposable
    {
        public RecordingProcessor()
        {
            CreatedCount++;
        }

        public static int CreatedCount { get; private set; }

        public static int CallCount { get; private set; }

        public static int DisposedCount { get; private set; }

        public static void Reset()
        {
            CreatedCount = 0;
            CallCount = 0;
            DisposedCount = 0;
        }

        public ValueTask ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            DisposedCount++;
        }
    }

    private sealed record FirstEvent;

    private sealed record SecondEvent;

    private sealed class NoOpConsumer : IEventConsumer<FirstEvent>
    {
        public ValueTask ConsumeAsync(
            FirstEvent @event,
            CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpStore : ITinyOutboxStore
    {
        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IReadOnlyList<TinyOutboxMessage>>(
                Array.Empty<TinyOutboxMessage>());
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask MarkFailedAsync(
            Guid messageId,
            string workerId,
            string error,
            int attemptCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

}
