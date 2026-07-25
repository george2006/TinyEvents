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
