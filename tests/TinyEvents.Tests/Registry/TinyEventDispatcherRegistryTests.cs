using Xunit;

namespace TinyEvents.Tests;

public sealed class TinyEventDispatcherRegistryTests
{
    [Fact]
    public void Resolve_returns_dispatcher_registered_with_current_event_name()
    {
        var dispatcher = new TinyEventDispatcher<UserCreated>();
        var registry = BuildRegistry(dispatcher);

        var resolved = registry.Resolve(typeof(UserCreated).FullName!);

        Assert.Same(dispatcher, resolved);
    }

    [Fact]
    public void Resolve_returns_dispatcher_registered_with_previous_event_name()
    {
        const string previousEventName = "Previous.Namespace.UserCreated";
        var dispatcher = new TinyEventDispatcher<UserCreated>();
        var registry = BuildRegistry(
            dispatcher,
            new TinyEventNameAlias(previousEventName, typeof(UserCreated)));

        var resolved = registry.Resolve(previousEventName);

        Assert.Same(dispatcher, resolved);
    }

    [Fact]
    public void Constructor_allows_duplicate_name_for_same_event_type()
    {
        const string previousEventName = "Previous.Namespace.UserCreated";
        var dispatcher = new TinyEventDispatcher<UserCreated>();
        var aliases = new[]
        {
            new TinyEventNameAlias(previousEventName, typeof(UserCreated)),
            new TinyEventNameAlias(previousEventName, typeof(UserCreated))
        };

        var registry = new TinyEventDispatcherRegistry(new[] { dispatcher }, aliases);

        Assert.Same(dispatcher, registry.Resolve(previousEventName));
    }

    [Fact]
    public void Constructor_rejects_previous_name_for_event_without_dispatcher()
    {
        var alias = new TinyEventNameAlias(
            "Previous.Namespace.UnregisteredEvent",
            typeof(UnregisteredEvent));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BuildRegistry(new TinyEventDispatcher<UserCreated>(), alias));

        Assert.Contains(typeof(UnregisteredEvent).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_rejects_previous_name_that_hides_current_event_name()
    {
        var userCreatedDispatcher = new TinyEventDispatcher<UserCreated>();
        var otherEventDispatcher = new TinyEventDispatcher<OtherEvent>();
        var alias = new TinyEventNameAlias(
            typeof(OtherEvent).FullName!,
            typeof(UserCreated));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TinyEventDispatcherRegistry(
                new ITinyEventDispatcher[] { userCreatedDispatcher, otherEventDispatcher },
                new[] { alias }));

        Assert.Contains(typeof(OtherEvent).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_rejects_unknown_event_name()
    {
        var registry = BuildRegistry(new TinyEventDispatcher<UserCreated>());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("Unknown.Event"));

        Assert.Contains("Unknown.Event", exception.Message, StringComparison.Ordinal);
    }

    private static TinyEventDispatcherRegistry BuildRegistry(
        ITinyEventDispatcher dispatcher,
        params TinyEventNameAlias[] aliases)
    {
        return new TinyEventDispatcherRegistry(new[] { dispatcher }, aliases);
    }

    private sealed record UserCreated(Guid UserId);

    private sealed record OtherEvent(Guid Id);

    private sealed record UnregisteredEvent(Guid Id);
}
