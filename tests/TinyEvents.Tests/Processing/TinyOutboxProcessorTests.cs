using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TinyEvents.Tests;

public sealed class TinyOutboxProcessorTests
{
    [Fact]
    public async Task Process_pending_async_claims_pending_messages_before_processing()
    {
        var store = new InMemoryTinyOutboxStore();
        var eventInstance = new UserCreated(Guid.NewGuid(), "user@example.com");
        await store.AddAsync(NewPendingMessage(eventInstance), CancellationToken.None);
        var processor = BuildProcessor(store);

        await processor.ProcessPendingAsync();

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Processed, message.Status);
        Assert.Equal("worker-1", message.ClaimedBy);
    }

    [Fact]
    public async Task Process_pending_async_generates_worker_id_when_not_configured()
    {
        var store = new RecordingClaimStore();
        var processor = BuildProcessor(store, workerId: null);

        await processor.ProcessPendingAsync();

        Assert.StartsWith("tiny-events-", Assert.Single(store.WorkerIds), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Process_pending_async_uses_configured_worker_id()
    {
        var store = new RecordingClaimStore();
        var processor = BuildProcessor(store, workerId: "configured-worker");

        await processor.ProcessPendingAsync();

        Assert.Equal("configured-worker", Assert.Single(store.WorkerIds));
    }

    [Fact]
    public async Task Process_pending_async_uses_same_generated_worker_id_for_lifetime()
    {
        var store = new RecordingClaimStore();
        var processor = BuildProcessor(store, workerId: null);

        await processor.ProcessPendingAsync();
        await processor.ProcessPendingAsync();

        Assert.Equal(2, store.WorkerIds.Count);
        Assert.Equal(store.WorkerIds[0], store.WorkerIds[1]);
    }

    [Fact]
    public async Task Process_pending_async_uses_configured_batch_size_and_claim_timeout()
    {
        var now = new DateTimeOffset(2026, 5, 31, 8, 0, 0, TimeSpan.Zero);
        var store = new RecordingClaimStore();
        var processor = BuildProcessor(
            store,
            workerId: "worker-1",
            batchSize: 7,
            claimTimeout: TimeSpan.FromSeconds(45),
            timeProvider: new FixedTimeProvider(now));

        await processor.ProcessPendingAsync();

        Assert.Equal(7, store.MaxCounts.Single());
        Assert.Equal(now, store.ClaimedAtValues.Single());
        Assert.Equal(TimeSpan.FromSeconds(45), store.ClaimTimeouts.Single());
    }

    [Fact]
    public void Options_reject_empty_configured_worker_id()
    {
        Assert.Throws<ArgumentException>(() => new TinyEventsOptions { WorkerId = " " });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_reject_non_positive_batch_size(int batchSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TinyEventsOptions { BatchSize = batchSize });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_reject_non_positive_max_attempts(int maxAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TinyEventsOptions { MaxAttempts = maxAttempts });
    }

    [Fact]
    public void Options_reject_negative_retry_delay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TinyEventsOptions { RetryDelay = TimeSpan.FromMilliseconds(-1) });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_reject_non_positive_claim_timeout(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TinyEventsOptions { ClaimTimeout = TimeSpan.FromMilliseconds(milliseconds) });
    }

    [Fact]
    public void Options_allow_zero_retry_delay()
    {
        var options = new TinyEventsOptions
        {
            RetryDelay = TimeSpan.Zero
        };

        Assert.Equal(TimeSpan.Zero, options.RetryDelay);
    }

    [Fact]
    public async Task Process_pending_async_invokes_matching_consumer()
    {
        RecordingConsumer.Consumed.Clear();
        var store = new InMemoryTinyOutboxStore();
        var eventInstance = new UserCreated(Guid.NewGuid(), "user@example.com");
        await store.AddAsync(NewPendingMessage(eventInstance), CancellationToken.None);
        var processor = BuildProcessor(store);

        await processor.ProcessPendingAsync();

        var consumed = Assert.Single(RecordingConsumer.Consumed);
        Assert.Equal(eventInstance.UserId, consumed.UserId);
        Assert.Equal(eventInstance.Email, consumed.Email);
    }

    [Fact]
    public async Task Process_pending_async_invokes_multiple_consumers_for_same_event()
    {
        RecordingConsumer.Consumed.Clear();
        SecondRecordingConsumer.Consumed.Clear();
        var store = new InMemoryTinyOutboxStore();
        await store.AddAsync(NewPendingMessage(new UserCreated(Guid.NewGuid(), "user@example.com")), CancellationToken.None);
        var processor = BuildProcessor(store, includeSecondConsumer: true);

        await processor.ProcessPendingAsync();

        Assert.Single(RecordingConsumer.Consumed);
        Assert.Single(SecondRecordingConsumer.Consumed);
    }

    [Fact]
    public async Task Process_pending_async_marks_failed_and_schedules_retry_when_consumer_throws()
    {
        ThrowingConsumer.Throw = true;
        var now = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryTinyOutboxStore();
        await store.AddAsync(NewPendingMessage(new UserCreated(Guid.NewGuid(), "user@example.com")), CancellationToken.None);
        var processor = BuildProcessor(
            store,
            includeThrowingConsumer: true,
            timeProvider: new FixedTimeProvider(now));

        await processor.ProcessPendingAsync();

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Pending, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("consumer failed", message.LastError);
        Assert.Equal(now.AddSeconds(30), message.NextAttemptAtUtc);
        ThrowingConsumer.Throw = false;
    }

    [Fact]
    public async Task Process_pending_async_logs_recorded_processing_failure()
    {
        ThrowingConsumer.Throw = true;
        var logger = new RecordingLogger<TinyOutboxProcessor>();
        var store = new InMemoryTinyOutboxStore();
        await store.AddAsync(NewPendingMessage(new UserCreated(Guid.NewGuid(), "user@example.com")), CancellationToken.None);
        var processor = BuildProcessor(
            store,
            includeThrowingConsumer: true,
            logger: logger);

        await processor.ProcessPendingAsync();

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(1202, entry.EventId.Id);
        Assert.Equal("EventProcessingFailed", entry.EventId.Name);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Contains("failed processing", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("user@example.com", entry.Message, StringComparison.Ordinal);
        Assert.Equal("worker-1", entry.Properties["WorkerId"]);
        Assert.Equal(1, entry.Properties["Attempt"]);
        Assert.Equal(typeof(UserCreated).FullName, entry.Properties["EventType"]);
        Assert.IsType<Guid>(entry.Properties["MessageId"]);
        Assert.IsType<DateTimeOffset>(entry.Properties["NextAttemptAtUtc"]);
        Assert.DoesNotContain(
            entry.Properties.Values,
            value => string.Equals(
                value?.ToString(),
                "user@example.com",
                StringComparison.Ordinal));
        Assert.IsType<InvalidOperationException>(entry.Exception);
        ThrowingConsumer.Throw = false;
    }

    [Fact]
    public async Task Process_pending_async_marks_processed_with_current_worker_id()
    {
        var eventInstance = new UserCreated(Guid.NewGuid(), "user@example.com");
        var store = new RecordingClaimStore(NewProcessingMessage(eventInstance));
        var processor = BuildProcessor(store, workerId: "worker-42");

        await processor.ProcessPendingAsync();

        Assert.Equal("worker-42", Assert.Single(store.ProcessedWorkerIds));
    }

    [Fact]
    public async Task Process_pending_async_does_not_mark_failed_when_mark_processed_fails()
    {
        RecordingConsumer.Consumed.Clear();
        var message = NewProcessingMessage(new UserCreated(Guid.NewGuid(), "user@example.com"));
        var store = new FailingMarkProcessedStore(message);
        var processor = BuildProcessor(store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await processor.ProcessPendingAsync());

        Assert.Equal("database failed while marking processed", exception.Message);
        Assert.Single(RecordingConsumer.Consumed);
        Assert.Equal(0, store.MarkFailedCount);
    }

    [Fact]
    public async Task Process_pending_async_marks_failed_with_current_worker_id()
    {
        ThrowingConsumer.Throw = true;
        var eventInstance = new UserCreated(Guid.NewGuid(), "user@example.com");
        var store = new RecordingClaimStore(NewProcessingMessage(eventInstance));
        var processor = BuildProcessor(
            store,
            workerId: "worker-42",
            includeThrowingConsumer: true);

        await processor.ProcessPendingAsync();

        Assert.Equal("worker-42", Assert.Single(store.FailedWorkerIds));
        ThrowingConsumer.Throw = false;
    }

    [Fact]
    public async Task Process_pending_async_propagates_failure_persistence_errors()
    {
        ThrowingConsumer.Throw = true;
        var message = NewProcessingMessage(new UserCreated(Guid.NewGuid(), "user@example.com"));
        var store = new FailingMarkFailedStore(message);
        var processor = BuildProcessor(store, includeThrowingConsumer: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await processor.ProcessPendingAsync());

        Assert.Equal("database failed while marking failed", exception.Message);
        Assert.Equal(1, store.MarkFailedCount);
        ThrowingConsumer.Throw = false;
    }

    [Fact]
    public async Task Process_pending_async_retries_expired_claim_after_worker_crash_simulation()
    {
        RecordingConsumer.Consumed.Clear();
        var store = new InMemoryTinyOutboxStore();
        var now = new DateTimeOffset(2026, 5, 31, 8, 0, 0, TimeSpan.Zero);
        await store.AddAsync(
            NewProcessingMessage(
                new UserCreated(Guid.NewGuid(), "user@example.com"),
                workerId: "dead-worker",
                claimExpiresAtUtc: now.AddSeconds(-1)),
            CancellationToken.None);
        var processor = BuildProcessor(
            store,
            workerId: "new-worker",
            timeProvider: new FixedTimeProvider(now));

        await processor.ProcessPendingAsync();

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Processed, message.Status);
        Assert.Equal("new-worker", message.ClaimedBy);
        Assert.Single(RecordingConsumer.Consumed);
    }

    [Fact]
    public async Task Process_pending_async_marks_failed_without_retry_after_max_attempts()
    {
        ThrowingConsumer.Throw = true;
        var logger = new RecordingLogger<TinyOutboxProcessor>();
        var store = new InMemoryTinyOutboxStore();
        await store.AddAsync(
            NewPendingMessage(new UserCreated(Guid.NewGuid(), "user@example.com"), attemptCount: 4),
            CancellationToken.None);
        var processor = BuildProcessor(
            store,
            includeThrowingConsumer: true,
            logger: logger);

        await processor.ProcessPendingAsync();

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Failed, message.Status);
        Assert.Equal(5, message.AttemptCount);
        Assert.Null(message.NextAttemptAtUtc);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(1204, entry.EventId.Id);
        Assert.Equal("EventRetriesExhausted", entry.EventId.Name);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Contains("at attempt 5 of 5", entry.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        ThrowingConsumer.Throw = false;
    }

    [Fact]
    public async Task Process_pending_async_marks_unknown_event_type_failed()
    {
        var store = new InMemoryTinyOutboxStore();
        await store.AddAsync(
            NewPendingMessage(
                eventType: "Missing.Event",
                payload: "{}"),
            CancellationToken.None);
        var processor = BuildProcessor(store);

        await processor.ProcessPendingAsync();

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Pending, message.Status);
        Assert.Equal("Event type 'Missing.Event' is not registered.", message.LastError);
    }

    [Fact]
    public async Task Process_pending_async_passes_cancellation_token_to_consumer()
    {
        CancellationRecordingConsumer.CancellationToken = default;
        var store = new InMemoryTinyOutboxStore();
        await store.AddAsync(NewPendingMessage(new UserCreated(Guid.NewGuid(), "user@example.com")), CancellationToken.None);
        var processor = BuildProcessor(store, includeCancellationConsumer: true);
        using var cancellation = new CancellationTokenSource();

        await processor.ProcessPendingAsync(cancellation.Token);

        Assert.Equal(cancellation.Token, CancellationRecordingConsumer.CancellationToken);
    }

    [Fact]
    public async Task Process_pending_async_stops_before_processing_when_canceled_after_claim()
    {
        RecordingConsumer.Consumed.Clear();
        using var cancellation = new CancellationTokenSource();
        var store = new CancelAfterClaimStore(
            cancellation,
            NewProcessingMessage(new UserCreated(Guid.NewGuid(), "user@example.com")));
        var processor = BuildProcessor(store);

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await processor.ProcessPendingAsync(cancellation.Token));

        Assert.Empty(RecordingConsumer.Consumed);
        Assert.Equal(0, store.MarkFailedCount);
    }

    [Fact]
    public async Task Process_pending_async_propagates_cancellation_during_claim()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new CancelDuringClaimStore(cancellation);
        var processor = BuildProcessor(store);

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await processor.ProcessPendingAsync(cancellation.Token));

        Assert.Equal(0, store.MarkFailedCount);
    }

    [Fact]
    public async Task Process_pending_async_propagates_cancellation_during_failure_persistence()
    {
        ThrowingConsumer.Throw = true;
        using var cancellation = new CancellationTokenSource();
        var message = NewProcessingMessage(new UserCreated(Guid.NewGuid(), "user@example.com"));
        var store = new CancelDuringMarkFailedStore(cancellation, message);
        var processor = BuildProcessor(store, includeThrowingConsumer: true);

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await processor.ProcessPendingAsync(cancellation.Token));

        Assert.Equal(1, store.MarkFailedCount);
        ThrowingConsumer.Throw = false;
    }

    [Fact]
    public async Task Process_pending_async_continues_when_mark_processed_loses_lease()
    {
        RecordingConsumer.Consumed.Clear();
        var logger = new RecordingLogger<TinyOutboxProcessor>();
        var firstMessage = NewProcessingMessage(new UserCreated(Guid.NewGuid(), "first@example.com"));
        var secondMessage = NewProcessingMessage(new UserCreated(Guid.NewGuid(), "second@example.com"));
        var store = new LeaseLostOnFirstProcessedStore(firstMessage, secondMessage);
        var processor = BuildProcessor(store, logger: logger);

        await processor.ProcessPendingAsync();

        Assert.Equal(2, RecordingConsumer.Consumed.Count);
        Assert.Equal(secondMessage.Id, Assert.Single(store.ProcessedMessageIds));
        Assert.Equal(0, store.MarkFailedCount);
        Assert.Contains(logger.Entries, entry =>
            entry.EventId.Id == 1300
            && entry.EventId.Name == "LeaseLost"
            && entry.LogLevel == LogLevel.Warning
            && entry.Message.Contains("lost its processing lease", StringComparison.Ordinal)
            && entry.Exception is TinyOutboxLeaseLostException);
    }

    [Fact]
    public async Task Process_pending_async_continues_when_mark_failed_loses_lease()
    {
        ThrowingConsumer.Throw = true;
        var logger = new RecordingLogger<TinyOutboxProcessor>();
        var message = NewProcessingMessage(new UserCreated(Guid.NewGuid(), "user@example.com"));
        var store = new LeaseLostOnFailedStore(message);
        var processor = BuildProcessor(store, includeThrowingConsumer: true, logger: logger);

        await processor.ProcessPendingAsync();

        Assert.Equal(1, store.MarkFailedCount);
        Assert.Contains(logger.Entries, entry =>
            entry.EventId.Id == 1300
            && entry.EventId.Name == "LeaseLost"
            && entry.LogLevel == LogLevel.Warning
            && entry.Message.Contains("lost its processing lease", StringComparison.Ordinal)
            && entry.Exception is TinyOutboxLeaseLostException);
        ThrowingConsumer.Throw = false;
    }

    private static ITinyOutboxProcessor BuildProcessor(
        ITinyOutboxStore store,
        bool includeSecondConsumer = false,
        bool includeThrowingConsumer = false,
        bool includeCancellationConsumer = false,
        TimeProvider? timeProvider = null,
        string? workerId = "worker-1",
        int batchSize = 10,
        TimeSpan? claimTimeout = null,
        ILogger<TinyOutboxProcessor>? logger = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton<ITinyOutboxStore>(store);
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton(new TinyEventsOptions
        {
            WorkerId = workerId,
            BatchSize = batchSize,
            MaxAttempts = 5,
            RetryDelay = TimeSpan.FromSeconds(30),
            ClaimTimeout = claimTimeout ?? TimeSpan.FromMinutes(5)
        });
        services.AddSingleton<ITinyEventSerializer, SystemTextJsonTinyEventSerializer>();
        services.AddSingleton<ITinyEventDispatcher>(
            new TinyEventDispatcher<UserCreated>(typeof(UserCreated).FullName!));
        services.AddSingleton<ITinyOutboxProcessor, TinyOutboxProcessor>();
        services.AddSingleton<IEventConsumer<UserCreated>, RecordingConsumer>();

        if (logger is not null)
        {
            services.AddSingleton(logger);
        }

        if (includeSecondConsumer)
        {
            services.AddSingleton<IEventConsumer<UserCreated>, SecondRecordingConsumer>();
        }

        if (includeThrowingConsumer)
        {
            services.AddSingleton<IEventConsumer<UserCreated>, ThrowingConsumer>();
        }

        if (includeCancellationConsumer)
        {
            services.AddSingleton<IEventConsumer<UserCreated>, CancellationRecordingConsumer>();
        }

        return services.BuildServiceProvider().GetRequiredService<ITinyOutboxProcessor>();
    }

    private static TinyOutboxMessage NewPendingMessage(
        UserCreated eventInstance,
        int attemptCount = 0)
    {
        var serializer = new SystemTextJsonTinyEventSerializer();

        return NewPendingMessage(
            typeof(UserCreated).FullName!,
            serializer.Serialize(eventInstance, typeof(UserCreated)),
            attemptCount);
    }

    private static TinyOutboxMessage NewProcessingMessage(
        UserCreated eventInstance,
        string workerId = "worker-1",
        DateTimeOffset? claimExpiresAtUtc = null)
    {
        var serializer = new SystemTextJsonTinyEventSerializer();

        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(UserCreated).FullName!,
            Payload = serializer.Serialize(eventInstance, typeof(UserCreated)),
            Status = TinyOutboxMessageStatus.Processing,
            AttemptCount = 0,
            ClaimedBy = workerId,
            ClaimedAtUtc = DateTimeOffset.UtcNow,
            ClaimExpiresAtUtc = claimExpiresAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(5),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static TinyOutboxMessage NewPendingMessage(
        string eventType,
        string payload,
        int attemptCount = 0)
    {
        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = payload,
            Status = TinyOutboxMessageStatus.Pending,
            AttemptCount = attemptCount,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
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

    private sealed record UserCreated(Guid UserId, string Email);

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

    private sealed class ThrowingConsumer : IEventConsumer<UserCreated>
    {
        public static bool Throw { get; set; }

        public ValueTask ConsumeAsync(UserCreated @event, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("consumer failed");
            }

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

    private sealed class RecordingClaimStore : ITinyOutboxStore
    {
        private readonly IReadOnlyList<TinyOutboxMessage> claimedMessages;

        public RecordingClaimStore(params TinyOutboxMessage[] claimedMessages)
        {
            this.claimedMessages = claimedMessages;
        }

        public List<int> MaxCounts { get; } = new List<int>();

        public List<string> WorkerIds { get; } = new List<string>();

        public List<DateTimeOffset> ClaimedAtValues { get; } = new List<DateTimeOffset>();

        public List<TimeSpan> ClaimTimeouts { get; } = new List<TimeSpan>();

        public List<string> ProcessedWorkerIds { get; } = new List<string>();

        public List<string> FailedWorkerIds { get; } = new List<string>();

        public ValueTask AddAsync(
            TinyOutboxMessage message,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            MaxCounts.Add(maxCount);
            WorkerIds.Add(workerId);
            ClaimedAtValues.Add(now);
            ClaimTimeouts.Add(claimTimeout);
            return ValueTask.FromResult(claimedMessages);
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            ProcessedWorkerIds.Add(workerId);
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
            FailedWorkerIds.Add(workerId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancelAfterClaimStore : ITinyOutboxStore
    {
        private readonly CancellationTokenSource cancellation;
        private readonly IReadOnlyList<TinyOutboxMessage> claimedMessages;

        public CancelAfterClaimStore(
            CancellationTokenSource cancellation,
            params TinyOutboxMessage[] claimedMessages)
        {
            this.cancellation = cancellation;
            this.claimedMessages = claimedMessages;
        }

        public int MarkFailedCount { get; private set; }

        public ValueTask AddAsync(
            TinyOutboxMessage message,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return ValueTask.FromResult(claimedMessages);
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Canceled processing should not mark messages as processed.");
        }

        public ValueTask MarkFailedAsync(
            Guid messageId,
            string workerId,
            string error,
            int attemptCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            MarkFailedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancelDuringClaimStore : ITinyOutboxStore
    {
        private readonly CancellationTokenSource cancellation;

        public CancelDuringClaimStore(CancellationTokenSource cancellation)
        {
            this.cancellation = cancellation;
        }

        public int MarkFailedCount { get; private set; }

        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            this.cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation should have been observed.");
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Canceled claiming should not process messages.");
        }

        public ValueTask MarkFailedAsync(
            Guid messageId,
            string workerId,
            string error,
            int attemptCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            MarkFailedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancelDuringMarkFailedStore : ITinyOutboxStore
    {
        private readonly CancellationTokenSource cancellation;
        private readonly IReadOnlyList<TinyOutboxMessage> claimedMessages;

        public CancelDuringMarkFailedStore(
            CancellationTokenSource cancellation,
            params TinyOutboxMessage[] claimedMessages)
        {
            this.cancellation = cancellation;
            this.claimedMessages = claimedMessages;
        }

        public int MarkFailedCount { get; private set; }

        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(claimedMessages);
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A failed consumer must not be marked as processed.");
        }

        public ValueTask MarkFailedAsync(
            Guid messageId,
            string workerId,
            string error,
            int attemptCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            MarkFailedCount++;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation should have been observed.");
        }
    }

    private sealed class FailingMarkProcessedStore : ITinyOutboxStore
    {
        private readonly IReadOnlyList<TinyOutboxMessage> claimedMessages;

        public FailingMarkProcessedStore(params TinyOutboxMessage[] claimedMessages)
        {
            this.claimedMessages = claimedMessages;
        }

        public int MarkFailedCount { get; private set; }

        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(claimedMessages);
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("database failed while marking processed");
        }

        public ValueTask MarkFailedAsync(
            Guid messageId,
            string workerId,
            string error,
            int attemptCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            MarkFailedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingMarkFailedStore : ITinyOutboxStore
    {
        private readonly IReadOnlyList<TinyOutboxMessage> claimedMessages;

        public FailingMarkFailedStore(params TinyOutboxMessage[] claimedMessages)
        {
            this.claimedMessages = claimedMessages;
        }

        public int MarkFailedCount { get; private set; }

        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(claimedMessages);
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A failed consumer must not be marked as processed.");
        }

        public ValueTask MarkFailedAsync(
            Guid messageId,
            string workerId,
            string error,
            int attemptCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            MarkFailedCount++;
            throw new InvalidOperationException("database failed while marking failed");
        }
    }

    private sealed class LeaseLostOnFirstProcessedStore : ITinyOutboxStore
    {
        private readonly IReadOnlyList<TinyOutboxMessage> claimedMessages;
        private bool hasLostLease;

        public LeaseLostOnFirstProcessedStore(params TinyOutboxMessage[] claimedMessages)
        {
            this.claimedMessages = claimedMessages;
        }

        public List<Guid> ProcessedMessageIds { get; } = new List<Guid>();

        public int MarkFailedCount { get; private set; }

        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(claimedMessages);
        }

        public ValueTask MarkProcessedAsync(
            Guid messageId,
            string workerId,
            DateTimeOffset processedAtUtc,
            CancellationToken cancellationToken)
        {
            if (!hasLostLease)
            {
                hasLostLease = true;
                throw new TinyOutboxLeaseLostException(messageId, workerId, "processed");
            }

            ProcessedMessageIds.Add(messageId);
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
            MarkFailedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LeaseLostOnFailedStore : ITinyOutboxStore
    {
        private readonly IReadOnlyList<TinyOutboxMessage> claimedMessages;

        public LeaseLostOnFailedStore(params TinyOutboxMessage[] claimedMessages)
        {
            this.claimedMessages = claimedMessages;
        }

        public int MarkFailedCount { get; private set; }

        public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
            int maxCount,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(claimedMessages);
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
            MarkFailedCount++;
            throw new TinyOutboxLeaseLostException(messageId, workerId, "failed");
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new List<LogEntry>();

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
            Entries.Add(new LogEntry(
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
