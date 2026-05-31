using TinyEvents;

namespace TinyEvents.Tests;

public sealed class InMemoryTinyOutboxStore : ITinyOutboxStore, ITinyOutboxWriter
{
    private readonly object syncRoot = new object();
    private readonly List<TinyOutboxMessage> messages = new List<TinyOutboxMessage>();

    public ValueTask AddAsync(
        TinyOutboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            messages.Add(Copy(message));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
        int maxCount,
        string workerId,
        DateTimeOffset now,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken)
    {
        if (workerId is null)
        {
            throw new ArgumentNullException(nameof(workerId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            var claimed = ClaimMessages(maxCount, workerId, now, claimTimeout);
            return ValueTask.FromResult<IReadOnlyList<TinyOutboxMessage>>(claimed);
        }
    }

    public ValueTask MarkProcessedAsync(
        Guid messageId,
        string workerId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken)
    {
        if (workerId is null)
        {
            throw new ArgumentNullException(nameof(workerId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            ReplaceClaimedMessage(
                messageId,
                workerId,
                message => MarkProcessed(message, processedAtUtc));
        }

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
        if (workerId is null)
        {
            throw new ArgumentNullException(nameof(workerId));
        }

        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            ReplaceClaimedMessage(
                messageId,
                workerId,
                message => MarkFailed(message, error, attemptCount, nextAttemptAtUtc));
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<TinyOutboxMessage> Snapshot()
    {
        lock (syncRoot)
        {
            return messages.Select(Copy).ToArray();
        }
    }

    private IReadOnlyList<TinyOutboxMessage> ClaimMessages(
        int maxCount,
        string workerId,
        DateTimeOffset now,
        TimeSpan claimTimeout)
    {
        var claimed = new List<TinyOutboxMessage>();

        for (var index = 0; index < messages.Count; index++)
        {
            if (claimed.Count == maxCount)
            {
                break;
            }

            var message = messages[index];

            if (!CanClaim(message, now))
            {
                continue;
            }

            var claimedMessage = Claim(message, workerId, now, claimTimeout);
            messages[index] = claimedMessage;
            claimed.Add(Copy(claimedMessage));
        }

        return claimed;
    }

    private static bool CanClaim(TinyOutboxMessage message, DateTimeOffset now)
    {
        if (message.Status == TinyOutboxMessageStatus.Pending)
        {
            return CanClaimPending(message, now);
        }

        if (message.Status == TinyOutboxMessageStatus.Processing)
        {
            return CanReclaimProcessing(message, now);
        }

        return false;
    }

    private static bool CanClaimPending(TinyOutboxMessage message, DateTimeOffset now)
    {
        return message.NextAttemptAtUtc is null || message.NextAttemptAtUtc <= now;
    }

    private static bool CanReclaimProcessing(TinyOutboxMessage message, DateTimeOffset now)
    {
        return message.ClaimExpiresAtUtc <= now;
    }

    private static TinyOutboxMessage Claim(
        TinyOutboxMessage message,
        string workerId,
        DateTimeOffset now,
        TimeSpan claimTimeout)
    {
        return Copy(
            message,
            status: TinyOutboxMessageStatus.Processing,
            claimedBy: workerId,
            claimedAtUtc: now,
            claimExpiresAtUtc: now.Add(claimTimeout));
    }

    private void ReplaceClaimedMessage(
        Guid messageId,
        string workerId,
        Func<TinyOutboxMessage, TinyOutboxMessage> replace)
    {
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];

            if (message.Id != messageId
                || message.ClaimedBy != workerId
                || message.Status != TinyOutboxMessageStatus.Processing)
            {
                continue;
            }

            messages[index] = replace(message);
            return;
        }
    }

    private static TinyOutboxMessage Copy(TinyOutboxMessage message)
    {
        return new TinyOutboxMessage
        {
            Id = message.Id,
            EventType = message.EventType,
            Payload = message.Payload,
            Status = message.Status,
            AttemptCount = message.AttemptCount,
            ClaimedBy = message.ClaimedBy,
            ClaimedAtUtc = message.ClaimedAtUtc,
            ClaimExpiresAtUtc = message.ClaimExpiresAtUtc,
            CreatedAtUtc = message.CreatedAtUtc,
            NextAttemptAtUtc = message.NextAttemptAtUtc,
            ProcessedAtUtc = message.ProcessedAtUtc,
            LastError = message.LastError
        };
    }

    private static TinyOutboxMessage Copy(
        TinyOutboxMessage message,
        TinyOutboxMessageStatus status,
        string? claimedBy = null,
        DateTimeOffset? claimedAtUtc = null,
        DateTimeOffset? claimExpiresAtUtc = null)
    {
        return new TinyOutboxMessage
        {
            Id = message.Id,
            EventType = message.EventType,
            Payload = message.Payload,
            Status = status,
            AttemptCount = message.AttemptCount,
            ClaimedBy = claimedBy,
            ClaimedAtUtc = claimedAtUtc,
            ClaimExpiresAtUtc = claimExpiresAtUtc,
            CreatedAtUtc = message.CreatedAtUtc,
            NextAttemptAtUtc = message.NextAttemptAtUtc,
            ProcessedAtUtc = message.ProcessedAtUtc,
            LastError = message.LastError
        };
    }

    private static TinyOutboxMessage MarkProcessed(
        TinyOutboxMessage message,
        DateTimeOffset processedAtUtc)
    {
        return new TinyOutboxMessage
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
            NextAttemptAtUtc = message.NextAttemptAtUtc,
            ProcessedAtUtc = processedAtUtc,
            LastError = message.LastError
        };
    }

    private static TinyOutboxMessage MarkFailed(
        TinyOutboxMessage message,
        string error,
        int attemptCount,
        DateTimeOffset? nextAttemptAtUtc)
    {
        return new TinyOutboxMessage
        {
            Id = message.Id,
            EventType = message.EventType,
            Payload = message.Payload,
            Status = GetFailureStatus(nextAttemptAtUtc),
            AttemptCount = attemptCount,
            ClaimedBy = message.ClaimedBy,
            ClaimedAtUtc = message.ClaimedAtUtc,
            ClaimExpiresAtUtc = message.ClaimExpiresAtUtc,
            CreatedAtUtc = message.CreatedAtUtc,
            NextAttemptAtUtc = nextAttemptAtUtc,
            ProcessedAtUtc = message.ProcessedAtUtc,
            LastError = error
        };
    }

    private static TinyOutboxMessageStatus GetFailureStatus(DateTimeOffset? nextAttemptAtUtc)
    {
        if (nextAttemptAtUtc is null)
        {
            return TinyOutboxMessageStatus.Failed;
        }

        return TinyOutboxMessageStatus.Pending;
    }
}
