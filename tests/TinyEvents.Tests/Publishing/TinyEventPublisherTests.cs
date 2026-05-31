using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TinyEvents.Tests;

public sealed class TinyEventPublisherTests
{
    [Fact]
    public async Task Publish_async_rejects_null_events()
    {
        var publisher = BuildPublisher();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await publisher.PublishAsync<UserCreated>(null!));
    }

    [Fact]
    public async Task Publish_async_stores_one_outbox_message()
    {
        var store = new RecordingOutboxStore();
        var publisher = BuildPublisher(store);

        await publisher.PublishAsync(new UserCreated(Guid.NewGuid(), "user@example.com"));

        Assert.Single(store.Messages);
    }

    [Fact]
    public async Task Publish_async_stores_event_type_name()
    {
        var store = new RecordingOutboxStore();
        var publisher = BuildPublisher(store);

        await publisher.PublishAsync(new UserCreated(Guid.NewGuid(), "user@example.com"));

        var message = Assert.Single(store.Messages);
        Assert.Equal(typeof(UserCreated).FullName, message.EventType);
    }

    [Fact]
    public async Task Publish_async_stores_serialized_payload()
    {
        var store = new RecordingOutboxStore();
        var publisher = BuildPublisher(store);
        var userId = Guid.NewGuid();

        await publisher.PublishAsync(new UserCreated(userId, "user@example.com"));

        var message = Assert.Single(store.Messages);
        Assert.Contains(userId.ToString(), message.Payload);
        Assert.Contains("user@example.com", message.Payload);
    }

    [Fact]
    public async Task Publish_async_stores_pending_message_with_created_time()
    {
        var store = new RecordingOutboxStore();
        var now = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        var publisher = BuildPublisher(store, new FixedTimeProvider(now));

        await publisher.PublishAsync(new UserCreated(Guid.NewGuid(), "user@example.com"));

        var message = Assert.Single(store.Messages);
        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(TinyOutboxMessageStatus.Pending, message.Status);
        Assert.Equal(now, message.CreatedAtUtc);
    }

    [Fact]
    public async Task Publish_async_passes_cancellation_token_to_store()
    {
        var store = new RecordingOutboxStore();
        var publisher = BuildPublisher(store);
        using var cancellation = new CancellationTokenSource();

        await publisher.PublishAsync(
            new UserCreated(Guid.NewGuid(), "user@example.com"),
            cancellation.Token);

        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task Publish_async_does_not_invoke_consumers_directly()
    {
        var consumer = new RecordingConsumer();
        var publisher = BuildPublisher();

        await publisher.PublishAsync(new UserCreated(Guid.NewGuid(), "user@example.com"));

        Assert.False(consumer.WasCalled);
    }

    [Fact]
    public async Task Use_tiny_events_resolves_publisher_with_explicit_writer()
    {
        var writer = new RecordingOutboxStore();
        var services = new ServiceCollection();

        services.UseTinyEvents();
        services.AddSingleton<ITinyOutboxWriter>(writer);

        using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<ITinyEventPublisher>();

        await publisher.PublishAsync(new UserCreated(Guid.NewGuid(), "user@example.com"));

        Assert.Single(writer.Messages);
    }

    private static TinyEventPublisher BuildPublisher()
    {
        return BuildPublisher(new RecordingOutboxStore());
    }

    private static TinyEventPublisher BuildPublisher(RecordingOutboxStore store)
    {
        return BuildPublisher(store, TimeProvider.System);
    }

    private static TinyEventPublisher BuildPublisher(
        RecordingOutboxStore store,
        TimeProvider timeProvider)
    {
        return new TinyEventPublisher(
            store,
            new SystemTextJsonTinyEventSerializer(),
            timeProvider);
    }

    private sealed class RecordingOutboxStore : ITinyOutboxWriter
    {
        public List<TinyOutboxMessage> Messages { get; } = new List<TinyOutboxMessage>();

        public CancellationToken CancellationToken { get; private set; }

        public ValueTask AddAsync(
            TinyOutboxMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            CancellationToken = cancellationToken;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class RecordingConsumer : IEventConsumer<UserCreated>
    {
        public bool WasCalled { get; private set; }

        public ValueTask ConsumeAsync(
            UserCreated @event,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record UserCreated(Guid UserId, string Email);
}
