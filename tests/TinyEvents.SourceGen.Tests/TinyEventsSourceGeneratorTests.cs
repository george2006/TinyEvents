using Xunit;

namespace TinyEvents.SourceGen.Tests;

public sealed class TinyEventsSourceGeneratorTests
{
    [Fact]
    public void Generated_registration_processes_event_at_runtime()
    {
        var assembly = SourceGeneratorTestHost.CompileAndLoad(
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyEvents;

            namespace MyApp.MultipleConsumers;

            public sealed record UserCreated(Guid UserId, string Email);

            public sealed class SendWelcomeEmail : IEventConsumer<UserCreated>
            {
                public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
                {
                    RuntimeProbe.Consumed.Add(@event.Email);
                    return ValueTask.CompletedTask;
                }
            }

            public static class RuntimeProbe
            {
                public static readonly List<string> Consumed = new List<string>();

                public static bool Run()
                {
                    var store = new ProbeOutboxStore();
                    var services = new ServiceCollection();

                    services.UseTinyEvents(options =>
                    {
                        options.WorkerId = "source-gen-runtime-test";
                    });
                    services.AddSingleton<ITinyOutboxStore>(store);
                    services.AddSingleton<ITinyOutboxWriter>(store);

                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();

                    var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
                    publisher.PublishAsync(new UserCreated(Guid.NewGuid(), "user@example.com")).AsTask().GetAwaiter().GetResult();

                    var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
                    processor.ProcessPendingAsync().AsTask().GetAwaiter().GetResult();

                    return Consumed.SequenceEqual(new[] { "user@example.com" })
                        && store.Snapshot().Single().Status == TinyOutboxMessageStatus.Processed;
                }
            }

            public sealed class ProbeOutboxStore : ITinyOutboxStore, ITinyOutboxWriter
            {
                private readonly List<TinyOutboxMessage> messages = new List<TinyOutboxMessage>();

                public ValueTask AddAsync(TinyOutboxMessage message, CancellationToken cancellationToken)
                {
                    messages.Add(message);
                    return ValueTask.CompletedTask;
                }

                public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
                    int maxCount,
                    string workerId,
                    DateTimeOffset now,
                    TimeSpan claimTimeout,
                    CancellationToken cancellationToken)
                {
                    for (var index = 0; index < messages.Count && index < maxCount; index++)
                    {
                        var message = messages[index];

                        messages[index] = new TinyOutboxMessage
                        {
                            Id = message.Id,
                            EventType = message.EventType,
                            Payload = message.Payload,
                            Status = TinyOutboxMessageStatus.Processing,
                            AttemptCount = message.AttemptCount,
                            ClaimedBy = workerId,
                            ClaimedAtUtc = now,
                            ClaimExpiresAtUtc = now.Add(claimTimeout),
                            CreatedAtUtc = message.CreatedAtUtc
                        };
                    }

                    return ValueTask.FromResult<IReadOnlyList<TinyOutboxMessage>>(messages
                        .Where(message => message.Status == TinyOutboxMessageStatus.Processing)
                        .ToArray());
                }

                public ValueTask MarkProcessedAsync(
                    Guid messageId,
                    string workerId,
                    DateTimeOffset processedAtUtc,
                    CancellationToken cancellationToken)
                {
                    Replace(messageId, workerId, TinyOutboxMessageStatus.Processed, processedAtUtc, null);
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
                    Replace(messageId, workerId, TinyOutboxMessageStatus.Failed, null, error);
                    return ValueTask.CompletedTask;
                }

                public IReadOnlyList<TinyOutboxMessage> Snapshot()
                {
                    return messages.ToArray();
                }

                private void Replace(
                    Guid messageId,
                    string workerId,
                    TinyOutboxMessageStatus status,
                    DateTimeOffset? processedAtUtc,
                    string? error)
                {
                    var index = messages.FindIndex(message => message.Id == messageId && message.ClaimedBy == workerId);
                    var message = messages[index];

                    messages[index] = new TinyOutboxMessage
                    {
                        Id = message.Id,
                        EventType = message.EventType,
                        Payload = message.Payload,
                        Status = status,
                        AttemptCount = message.AttemptCount,
                        ClaimedBy = message.ClaimedBy,
                        ClaimedAtUtc = message.ClaimedAtUtc,
                        ClaimExpiresAtUtc = message.ClaimExpiresAtUtc,
                        CreatedAtUtc = message.CreatedAtUtc,
                        ProcessedAtUtc = processedAtUtc,
                        LastError = error
                    };
                }
            }
            """);

        var probe = assembly.GetType("MyApp.MultipleConsumers.RuntimeProbe")!;
        var run = probe.GetMethod("Run")!;

        Assert.True((bool)run.Invoke(null, Array.Empty<object>())!);
    }

    [Fact]
    public void Generator_discovers_consumer_and_emits_registration()
    {
        var result = SourceGeneratorTestHost.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyEvents;

            namespace MyApp;

            public sealed record UserCreated(System.Guid UserId);

            public sealed class SendWelcomeEmail : IEventConsumer<UserCreated>
            {
                public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """);

        var source = Assert.Single(result.GeneratedTrees).ToString();

        Assert.Contains("IEventConsumer<global::MyApp.UserCreated>", source);
        Assert.Contains("global::MyApp.SendWelcomeEmail", source);
        Assert.Contains("TinyEventTypeDescriptor", source);
        Assert.Contains("\"MyApp.UserCreated\"", source);
        Assert.Contains("TinyEventsBootstrap.AddContribution", source);
    }

    [Fact]
    public void Generator_supports_multiple_consumers_for_same_event()
    {
        var result = SourceGeneratorTestHost.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyEvents;

            namespace MyApp;

            public sealed record UserCreated(System.Guid UserId);

            public sealed class SendWelcomeEmail : IEventConsumer<UserCreated>
            {
                public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }

            public sealed class UpdateProjection : IEventConsumer<UserCreated>
            {
                public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """);

        var source = Assert.Single(result.GeneratedTrees).ToString();

        Assert.Contains("global::MyApp.SendWelcomeEmail", source);
        Assert.Contains("global::MyApp.UpdateProjection", source);
        Assert.Equal(1, source.Split("TinyEventTypeDescriptor").Length - 1);
    }

    [Fact]
    public void Generator_supports_multiple_event_types()
    {
        var result = SourceGeneratorTestHost.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyEvents;

            namespace MyApp;

            public sealed record UserCreated(System.Guid UserId);
            public sealed record UserDeleted(System.Guid UserId);

            public sealed class SendWelcomeEmail : IEventConsumer<UserCreated>
            {
                public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }

            public sealed class CleanupUser : IEventConsumer<UserDeleted>
            {
                public ValueTask ConsumeAsync(UserDeleted @event, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """);

        var source = Assert.Single(result.GeneratedTrees).ToString();

        Assert.Contains("IEventConsumer<global::MyApp.UserCreated>", source);
        Assert.Contains("IEventConsumer<global::MyApp.UserDeleted>", source);
        Assert.Contains("\"MyApp.UserCreated\"", source);
        Assert.Contains("\"MyApp.UserDeleted\"", source);
    }

    [Fact]
    public void Generator_does_not_require_event_marker_interface()
    {
        var result = SourceGeneratorTestHost.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyEvents;

            namespace MyApp;

            public sealed record PlainEvent(System.Guid Id);

            public sealed class PlainEventConsumer : IEventConsumer<PlainEvent>
            {
                public ValueTask ConsumeAsync(PlainEvent @event, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """);

        var source = Assert.Single(result.GeneratedTrees).ToString();

        Assert.Contains("IEventConsumer<global::MyApp.PlainEvent>", source);
        Assert.Contains("global::MyApp.PlainEventConsumer", source);
    }

    [Fact]
    public void Generator_emits_nothing_without_consumers()
    {
        var result = SourceGeneratorTestHost.Run(
            """
            namespace MyApp;

            public sealed record UserCreated(System.Guid UserId);

            public sealed class NotAConsumer
            {
            }
            """);

        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generated_registration_invokes_multiple_consumers_at_runtime()
    {
        var assembly = SourceGeneratorTestHost.CompileAndLoad(
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyEvents;

            namespace MyApp;

            public sealed record UserCreated(Guid UserId, string Email);

            public sealed class FirstConsumer : IEventConsumer<UserCreated>
            {
                public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
                {
                    RuntimeProbe.Consumed.Add("first:" + @event.Email);
                    return ValueTask.CompletedTask;
                }
            }

            public sealed class SecondConsumer : IEventConsumer<UserCreated>
            {
                public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
                {
                    RuntimeProbe.Consumed.Add("second:" + @event.Email);
                    return ValueTask.CompletedTask;
                }
            }

            public static class RuntimeProbe
            {
                public static readonly List<string> Consumed = new List<string>();

                public static bool Run()
                {
                    var store = new ProbeOutboxStore();
                    var services = new ServiceCollection();

                    services.UseTinyEvents(options =>
                    {
                        options.WorkerId = "source-gen-runtime-test";
                    });
                    services.AddSingleton<ITinyOutboxStore>(store);
                    services.AddSingleton<ITinyOutboxWriter>(store);

                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();

                    var publisher = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
                    publisher.PublishAsync(new UserCreated(Guid.NewGuid(), "user@example.com")).AsTask().GetAwaiter().GetResult();

                    var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
                    processor.ProcessPendingAsync().AsTask().GetAwaiter().GetResult();

                    return Consumed.OrderBy(value => value).SequenceEqual(new[]
                    {
                        "first:user@example.com",
                        "second:user@example.com"
                    });
                }
            }

            public sealed class ProbeOutboxStore : ITinyOutboxStore, ITinyOutboxWriter
            {
                private readonly List<TinyOutboxMessage> messages = new List<TinyOutboxMessage>();

                public ValueTask AddAsync(TinyOutboxMessage message, CancellationToken cancellationToken)
                {
                    messages.Add(message);
                    return ValueTask.CompletedTask;
                }

                public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
                    int maxCount,
                    string workerId,
                    DateTimeOffset now,
                    TimeSpan claimTimeout,
                    CancellationToken cancellationToken)
                {
                    for (var index = 0; index < messages.Count && index < maxCount; index++)
                    {
                        var message = messages[index];

                        messages[index] = new TinyOutboxMessage
                        {
                            Id = message.Id,
                            EventType = message.EventType,
                            Payload = message.Payload,
                            Status = TinyOutboxMessageStatus.Processing,
                            AttemptCount = message.AttemptCount,
                            ClaimedBy = workerId,
                            ClaimedAtUtc = now,
                            ClaimExpiresAtUtc = now.Add(claimTimeout),
                            CreatedAtUtc = message.CreatedAtUtc
                        };
                    }

                    return ValueTask.FromResult<IReadOnlyList<TinyOutboxMessage>>(messages
                        .Where(message => message.Status == TinyOutboxMessageStatus.Processing)
                        .ToArray());
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
            """);

        var probe = assembly.GetType("MyApp.RuntimeProbe")!;
        var run = probe.GetMethod("Run")!;

        Assert.True((bool)run.Invoke(null, Array.Empty<object>())!);
    }

    [Fact]
    public void Generator_ignores_abstract_consumers()
    {
        var result = SourceGeneratorTestHost.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyEvents;

            namespace MyApp;

            public sealed record UserCreated(System.Guid UserId);

            public abstract class SendWelcomeEmail : IEventConsumer<UserCreated>
            {
                public abstract ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken);
            }
            """);

        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_reports_open_generic_consumers()
    {
        var result = SourceGeneratorTestHost.Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyEvents;

            namespace MyApp;

            public sealed class GenericConsumer<TEvent> : IEventConsumer<TEvent>
            {
                public ValueTask ConsumeAsync(TEvent @event, CancellationToken cancellationToken)
                {
                    return ValueTask.CompletedTask;
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal("TEV001", diagnostic.Id);
        Assert.Empty(result.GeneratedTrees);
    }
}
