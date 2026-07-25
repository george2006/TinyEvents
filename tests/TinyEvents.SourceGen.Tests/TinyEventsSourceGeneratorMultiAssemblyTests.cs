using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TinyEvents.SourceGen.Tests;

public sealed class TinyEventsSourceGeneratorMultiAssemblyTests
{
    [Fact]
    public async Task Loaded_consumer_assembly_contribution_processes_event_in_host_services()
    {
        var consumerAssembly = CompileExternalConsumerAssembly();
        var userId = Guid.NewGuid();
        var store = new ProbeOutboxStore();
        await AddExternalUserCreatedMessageAsync(store, consumerAssembly, userId);
        using var provider = BuildHostProvider(store);
        var processor = provider.GetRequiredService<ITinyOutboxProcessor>();

        await processor.ProcessPendingAsync();

        Assert.True(ExternalRuntimeProbeContains(consumerAssembly, userId));
        Assert.Equal(TinyOutboxMessageStatus.Processed, Assert.Single(store.Snapshot()).Status);
    }

    private static Assembly CompileExternalConsumerAssembly()
    {
        return SourceGeneratorTestHost.CompileAndLoad(
            """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using TinyEvents;

            namespace ExternalConsumers;

            public sealed record UserCreated(Guid UserId);

            public sealed class RecordUserCreated : IEventConsumer<UserCreated>
            {
                public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
                {
                    RuntimeProbe.Consumed.Add(@event.UserId);
                    return ValueTask.CompletedTask;
                }
            }

            public static class RuntimeProbe
            {
                public static readonly List<Guid> Consumed = new List<Guid>();

                public static bool Contains(Guid userId)
                {
                    return Consumed.Contains(userId);
                }
            }
            """);
    }

    private static async ValueTask AddExternalUserCreatedMessageAsync(
        ProbeOutboxStore store,
        Assembly consumerAssembly,
        Guid userId)
    {
        var eventType = consumerAssembly.GetType("ExternalConsumers.UserCreated")!;
        var eventInstance = Activator.CreateInstance(eventType, userId)!;
        var serializer = new SystemTextJsonTinyEventSerializer();

        await store.AddAsync(
            new TinyOutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = eventType.FullName!,
                Payload = serializer.Serialize(eventInstance, eventType),
                Status = TinyOutboxMessageStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            CancellationToken.None);
    }

    private static ServiceProvider BuildHostProvider(ProbeOutboxStore store)
    {
        var services = new ServiceCollection();
        services.UseTinyEvents(options =>
        {
            options.WorkerId = "host-worker";
        });
        services.AddSingleton<ITinyOutboxStore>(store);
        services.AddSingleton<ITinyOutboxWriter>(store);

        return services.BuildServiceProvider();
    }

    private static bool ExternalRuntimeProbeContains(
        Assembly consumerAssembly,
        Guid userId)
    {
        var runtimeProbe = consumerAssembly.GetType("ExternalConsumers.RuntimeProbe")!;
        var contains = runtimeProbe.GetMethod("Contains")!;
        return (bool)contains.Invoke(null, new object[] { userId })!;
    }

    private sealed class ProbeOutboxStore : ITinyOutboxStore, ITinyOutboxWriter
    {
        private readonly List<TinyOutboxMessage> messages = new List<TinyOutboxMessage>();

        public ValueTask AddAsync(
            TinyOutboxMessage message,
            CancellationToken cancellationToken)
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
            var claimedMessages = new List<TinyOutboxMessage>();

            for (var index = 0; index < messages.Count && claimedMessages.Count < maxCount; index++)
            {
                var message = messages[index];

                if (message.Status != TinyOutboxMessageStatus.Pending)
                {
                    continue;
                }

                var claimedMessage = Claim(message, workerId, now, claimTimeout);
                messages[index] = claimedMessage;
                claimedMessages.Add(claimedMessage);
            }

            return ValueTask.FromResult<IReadOnlyList<TinyOutboxMessage>>(claimedMessages);
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            Replace(
                messageId,
                message => new TinyOutboxMessage
                {
                    Id = message.Id,
                    EventType = message.EventType,
                    Payload = message.Payload,
                    Status = TinyOutboxMessageStatus.Processed,
                    AttemptCount = message.AttemptCount,
                    ClaimedBy = message.ClaimedBy,
                    ClaimedAtUtc = message.ClaimedAtUtc,
                    ClaimExpiresAtUtc = message.ClaimExpiresAtUtc,
                    CreatedAtUtc = message.CreatedAtUtc,
                    ProcessedAtUtc = processedAtUtc
                });

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
            Replace(
                messageId,
                message => new TinyOutboxMessage
                {
                    Id = message.Id,
                    EventType = message.EventType,
                    Payload = message.Payload,
                    Status = TinyOutboxMessageStatus.Pending,
                    AttemptCount = attemptCount,
                    ClaimedBy = message.ClaimedBy,
                    ClaimedAtUtc = message.ClaimedAtUtc,
                    ClaimExpiresAtUtc = message.ClaimExpiresAtUtc,
                    CreatedAtUtc = message.CreatedAtUtc,
                    NextAttemptAtUtc = nextAttemptAtUtc,
                    LastError = error
                });

            return ValueTask.CompletedTask;
        }

        public IReadOnlyList<TinyOutboxMessage> Snapshot()
        {
            return messages.ToArray();
        }

        private void Replace(
            Guid messageId,
            Func<TinyOutboxMessage, TinyOutboxMessage> replace)
        {
            var index = messages.FindIndex(message => message.Id == messageId);
            messages[index] = replace(messages[index]);
        }

        private TinyOutboxMessage Claim(
            TinyOutboxMessage message,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout)
        {
            return new TinyOutboxMessage
            {
                Id = message.Id,
                EventType = message.EventType,
                Payload = message.Payload,
                Status = TinyOutboxMessageStatus.Processing,
                AttemptCount = message.AttemptCount,
                ClaimedBy = workerId,
                ClaimedAtUtc = now,
                ClaimExpiresAtUtc = now.Add(claimTimeout),
                CreatedAtUtc = message.CreatedAtUtc,
                NextAttemptAtUtc = message.NextAttemptAtUtc,
                LastError = message.LastError
            };
        }
    }
}
