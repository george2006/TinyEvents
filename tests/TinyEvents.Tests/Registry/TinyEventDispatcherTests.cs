using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TinyEvents.Tests;

public sealed class TinyEventDispatcherTests
{
    [Fact]
    public void Default_constructor_uses_runtime_event_type_name()
    {
        var dispatcher = new TinyEventDispatcher<NestedEvents.UserCreated>();

        Assert.Equal(typeof(NestedEvents.UserCreated).FullName, dispatcher.EventTypeName);
    }

    [Fact]
    public void Constructor_rejects_empty_event_type_name()
    {
        Assert.Throws<ArgumentException>(() => new TinyEventDispatcher<UserCreated>(" "));
    }

    [Fact]
    public void Event_type_returns_generic_event_type()
    {
        var dispatcher = new TinyEventDispatcher<UserCreated>("test-event");

        Assert.Equal(typeof(UserCreated), dispatcher.EventType);
    }

    [Fact]
    public async Task Dispatch_async_invokes_matching_consumer()
    {
        RecordingConsumer.Consumed.Clear();
        var services = new ServiceCollection();
        services.AddSingleton<IEventConsumer<UserCreated>, RecordingConsumer>();
        using var provider = services.BuildServiceProvider();
        var dispatcher = new TinyEventDispatcher<UserCreated>("test-event");
        var eventInstance = new UserCreated(Guid.NewGuid());

        await dispatcher.DispatchAsync(provider, eventInstance, CancellationToken.None);

        Assert.Equal(eventInstance, Assert.Single(RecordingConsumer.Consumed));
    }

    [Fact]
    public async Task Dispatch_async_invokes_multiple_matching_consumers()
    {
        RecordingConsumer.Consumed.Clear();
        SecondRecordingConsumer.Consumed.Clear();
        var services = new ServiceCollection();
        services.AddSingleton<IEventConsumer<UserCreated>, RecordingConsumer>();
        services.AddSingleton<IEventConsumer<UserCreated>, SecondRecordingConsumer>();
        using var provider = services.BuildServiceProvider();
        var dispatcher = new TinyEventDispatcher<UserCreated>("test-event");
        var eventInstance = new UserCreated(Guid.NewGuid());

        await dispatcher.DispatchAsync(provider, eventInstance, CancellationToken.None);

        Assert.Equal(eventInstance, Assert.Single(RecordingConsumer.Consumed));
        Assert.Equal(eventInstance, Assert.Single(SecondRecordingConsumer.Consumed));
    }

    [Fact]
    public async Task Dispatch_async_passes_cancellation_token_to_consumers()
    {
        CancellationRecordingConsumer.CancellationToken = default;
        var services = new ServiceCollection();
        services.AddSingleton<IEventConsumer<UserCreated>, CancellationRecordingConsumer>();
        using var provider = services.BuildServiceProvider();
        var dispatcher = new TinyEventDispatcher<UserCreated>("test-event");
        using var cancellation = new CancellationTokenSource();

        await dispatcher.DispatchAsync(provider, new UserCreated(Guid.NewGuid()), cancellation.Token);

        Assert.Equal(cancellation.Token, CancellationRecordingConsumer.CancellationToken);
    }

    [Fact]
    public async Task Dispatch_async_rejects_null_service_provider()
    {
        var dispatcher = new TinyEventDispatcher<UserCreated>("test-event");

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await dispatcher.DispatchAsync(null!, new UserCreated(Guid.NewGuid()), CancellationToken.None));
    }

    private sealed record UserCreated(Guid UserId);

    private static class NestedEvents
    {
        public sealed record UserCreated(Guid UserId);
    }

    private sealed class RecordingConsumer : IEventConsumer<UserCreated>
    {
        public static List<UserCreated> Consumed { get; } = new List<UserCreated>();

        public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
        {
            Consumed.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SecondRecordingConsumer : IEventConsumer<UserCreated>
    {
        public static List<UserCreated> Consumed { get; } = new List<UserCreated>();

        public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
        {
            Consumed.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellationRecordingConsumer : IEventConsumer<UserCreated>
    {
        public static CancellationToken CancellationToken { get; set; }

        public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return ValueTask.CompletedTask;
        }
    }
}
