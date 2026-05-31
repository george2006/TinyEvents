using Xunit;

namespace TinyEvents.Tests;

public sealed class InMemoryTinyOutboxStoreTests
{
    [Fact]
    public async Task Add_async_stores_message_snapshot()
    {
        var store = new InMemoryTinyOutboxStore();
        var message = NewPendingMessage();

        await store.AddAsync(message, CancellationToken.None);

        var stored = Assert.Single(store.Snapshot());
        Assert.Equal(message.Id, stored.Id);
        Assert.Equal(message.EventType, stored.EventType);
        Assert.Equal(message.Payload, stored.Payload);
    }

    [Fact]
    public async Task Claim_pending_async_claims_pending_message_for_worker()
    {
        var store = new InMemoryTinyOutboxStore();
        var now = DateTimeOffset.UtcNow;
        await store.AddAsync(NewPendingMessage(), CancellationToken.None);

        var claimed = await store.ClaimPendingAsync(
            maxCount: 1,
            workerId: "worker-1",
            now: now,
            claimTimeout: TimeSpan.FromMinutes(5),
            cancellationToken: CancellationToken.None);

        var message = Assert.Single(claimed);
        Assert.Equal(TinyOutboxMessageStatus.Processing, message.Status);
        Assert.Equal("worker-1", message.ClaimedBy);
        Assert.Equal(now, message.ClaimedAtUtc);
        Assert.Equal(now.AddMinutes(5), message.ClaimExpiresAtUtc);
    }

    [Fact]
    public async Task Claim_pending_async_honors_batch_size()
    {
        var store = new InMemoryTinyOutboxStore();
        await store.AddAsync(NewPendingMessage(), CancellationToken.None);
        await store.AddAsync(NewPendingMessage(), CancellationToken.None);

        var claimed = await store.ClaimPendingAsync(
            maxCount: 1,
            workerId: "worker-1",
            now: DateTimeOffset.UtcNow,
            claimTimeout: TimeSpan.FromMinutes(5),
            cancellationToken: CancellationToken.None);

        Assert.Single(claimed);
    }

    [Fact]
    public async Task Claim_pending_async_ignores_future_retry_messages()
    {
        var store = new InMemoryTinyOutboxStore();
        var now = DateTimeOffset.UtcNow;
        await store.AddAsync(
            NewPendingMessage(nextAttemptAtUtc: now.AddMinutes(1)),
            CancellationToken.None);

        var claimed = await store.ClaimPendingAsync(
            maxCount: 1,
            workerId: "worker-1",
            now: now,
            claimTimeout: TimeSpan.FromMinutes(5),
            cancellationToken: CancellationToken.None);

        Assert.Empty(claimed);
    }

    [Fact]
    public async Task Claim_pending_async_does_not_claim_active_processing_message()
    {
        var store = new InMemoryTinyOutboxStore();
        var now = DateTimeOffset.UtcNow;
        await store.AddAsync(
            NewProcessingMessage(claimExpiresAtUtc: now.AddMinutes(1)),
            CancellationToken.None);

        var claimed = await store.ClaimPendingAsync(
            maxCount: 1,
            workerId: "worker-2",
            now: now,
            claimTimeout: TimeSpan.FromMinutes(5),
            cancellationToken: CancellationToken.None);

        Assert.Empty(claimed);
    }

    [Fact]
    public async Task Claim_pending_async_reclaims_expired_processing_message()
    {
        var store = new InMemoryTinyOutboxStore();
        var now = DateTimeOffset.UtcNow;
        await store.AddAsync(
            NewProcessingMessage(claimExpiresAtUtc: now.AddSeconds(-1)),
            CancellationToken.None);

        var claimed = await store.ClaimPendingAsync(
            maxCount: 1,
            workerId: "worker-2",
            now: now,
            claimTimeout: TimeSpan.FromMinutes(5),
            cancellationToken: CancellationToken.None);

        var message = Assert.Single(claimed);
        Assert.Equal("worker-2", message.ClaimedBy);
        Assert.Equal(now.AddMinutes(5), message.ClaimExpiresAtUtc);
    }

    [Fact]
    public async Task Mark_processed_async_updates_only_message_claimed_by_worker()
    {
        var store = new InMemoryTinyOutboxStore();
        var messageId = Guid.NewGuid();
        var processedAt = DateTimeOffset.UtcNow;
        await store.AddAsync(NewProcessingMessage(id: messageId, workerId: "worker-1"), CancellationToken.None);

        await store.MarkProcessedAsync(
            messageId,
            workerId: "worker-2",
            processedAtUtc: processedAt,
            cancellationToken: CancellationToken.None);

        Assert.Equal(TinyOutboxMessageStatus.Processing, Assert.Single(store.Snapshot()).Status);

        await store.MarkProcessedAsync(
            messageId,
            workerId: "worker-1",
            processedAtUtc: processedAt,
            cancellationToken: CancellationToken.None);

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Processed, message.Status);
        Assert.Equal(processedAt, message.ProcessedAtUtc);
    }

    [Fact]
    public async Task Mark_processed_async_updates_only_processing_messages()
    {
        var store = new InMemoryTinyOutboxStore();
        var messageId = Guid.NewGuid();
        await store.AddAsync(
            NewPendingMessage(id: messageId, claimedBy: "worker-1"),
            CancellationToken.None);

        await store.MarkProcessedAsync(
            messageId,
            workerId: "worker-1",
            processedAtUtc: DateTimeOffset.UtcNow,
            cancellationToken: CancellationToken.None);

        Assert.Equal(TinyOutboxMessageStatus.Pending, Assert.Single(store.Snapshot()).Status);
    }

    [Fact]
    public async Task Mark_failed_async_updates_only_message_claimed_by_worker()
    {
        var store = new InMemoryTinyOutboxStore();
        var messageId = Guid.NewGuid();
        var nextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(30);
        await store.AddAsync(NewProcessingMessage(id: messageId, workerId: "worker-1"), CancellationToken.None);

        await store.MarkFailedAsync(
            messageId,
            workerId: "worker-2",
            error: "nope",
            attemptCount: 1,
            nextAttemptAtUtc: nextAttemptAt,
            cancellationToken: CancellationToken.None);

        Assert.Equal(TinyOutboxMessageStatus.Processing, Assert.Single(store.Snapshot()).Status);

        await store.MarkFailedAsync(
            messageId,
            workerId: "worker-1",
            error: "boom",
            attemptCount: 2,
            nextAttemptAtUtc: nextAttemptAt,
            cancellationToken: CancellationToken.None);

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Pending, message.Status);
        Assert.Equal(2, message.AttemptCount);
        Assert.Equal("boom", message.LastError);
        Assert.Equal(nextAttemptAt, message.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Mark_failed_async_updates_only_processing_messages()
    {
        var store = new InMemoryTinyOutboxStore();
        var messageId = Guid.NewGuid();
        await store.AddAsync(
            NewPendingMessage(id: messageId, claimedBy: "worker-1"),
            CancellationToken.None);

        await store.MarkFailedAsync(
            messageId,
            workerId: "worker-1",
            error: "boom",
            attemptCount: 1,
            nextAttemptAtUtc: null,
            cancellationToken: CancellationToken.None);

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Pending, message.Status);
        Assert.Null(message.LastError);
    }

    [Fact]
    public async Task Mark_failed_async_marks_failed_when_no_retry_is_scheduled()
    {
        var store = new InMemoryTinyOutboxStore();
        var messageId = Guid.NewGuid();
        await store.AddAsync(NewProcessingMessage(id: messageId, workerId: "worker-1"), CancellationToken.None);

        await store.MarkFailedAsync(
            messageId,
            workerId: "worker-1",
            error: "boom",
            attemptCount: 5,
            nextAttemptAtUtc: null,
            cancellationToken: CancellationToken.None);

        var message = Assert.Single(store.Snapshot());
        Assert.Equal(TinyOutboxMessageStatus.Failed, message.Status);
        Assert.Null(message.NextAttemptAtUtc);
    }

    private static TinyOutboxMessage NewPendingMessage(
        DateTimeOffset? nextAttemptAtUtc = null,
        Guid? id = null,
        string? claimedBy = null)
    {
        return new TinyOutboxMessage
        {
            Id = id ?? Guid.NewGuid(),
            EventType = "TinyEvents.Tests.UserCreated",
            Payload = "{}",
            Status = TinyOutboxMessageStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            NextAttemptAtUtc = nextAttemptAtUtc,
            ClaimedBy = claimedBy
        };
    }

    private static TinyOutboxMessage NewProcessingMessage(
        Guid? id = null,
        string workerId = "worker-1",
        DateTimeOffset? claimExpiresAtUtc = null)
    {
        return new TinyOutboxMessage
        {
            Id = id ?? Guid.NewGuid(),
            EventType = "TinyEvents.Tests.UserCreated",
            Payload = "{}",
            Status = TinyOutboxMessageStatus.Processing,
            ClaimedBy = workerId,
            ClaimedAtUtc = DateTimeOffset.UtcNow,
            ClaimExpiresAtUtc = claimExpiresAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(5),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
