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

}
