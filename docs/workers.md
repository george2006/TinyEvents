# Workers and Leases

TinyEvents workers process durable outbox messages.

The worker model is lease-based. TinyEvents does not rely on stable worker ids for crash recovery.

## Claim Lifecycle

When a worker claims a message, the provider sets:

- `Status = Processing`
- `ClaimedBy = workerId`
- `ClaimedAtUtc = now`
- `ClaimExpiresAtUtc = now + ClaimTimeout`

Messages are claimable when:

- `Status = Pending` and `NextAttemptAtUtc` is null or not in the future
- `Status = Processing` and `ClaimExpiresAtUtc` has passed

Expired processing claims are the recovery mechanism.

## Worker Identity

Each active worker instance should have a unique worker id.

If no worker id is configured, TinyEvents generates one:

```text
tiny-events-{machineName}-{processId}-{guid}
```

Worker ids do not need to be stable across restarts.

Configure a worker id only when your hosting environment can provide a unique identity for each active worker:

```csharp
services.AddTinyEventsWorker(options =>
{
    options.WorkerId = "orders-worker-01";
});
```

Empty worker ids are rejected.

## Hosted Worker

> **Release status:** Processed-message cleanup is included in
> `0.1.0-beta.1`. Its defaults passed the cleanup behavior and active-load
> gates.

Install:

```bash
dotnet add package TinyEvents --version 0.1.0-beta.1
dotnet add package TinyEvents.Worker --version 0.1.0-beta.1
```

`TinyEvents.Worker` contains the hosted-service integration. You still need one outbox provider package, such as:

- `TinyEvents.SqlServer.EntityFrameworkCore`
- `TinyEvents.SqlServer.AdoNet`
- `TinyEvents.PostgreSql.EntityFrameworkCore`
- `TinyEvents.PostgreSql.AdoNet`

Register:

```csharp
using TinyEvents.Worker;

services.AddTinyEventsWorker(options =>
{
    options.BatchSize = 50;
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.ClaimTimeout = TimeSpan.FromMinutes(5);
    options.CleanupEnabled = true;
    options.ProcessedRetention = TimeSpan.FromHours(1);
    options.CleanupBatchSize = 1_000;
    options.CleanupInterval = TimeSpan.FromSeconds(1);
});
```

The cleanup values shown above are the accepted beta defaults.
They remain configurable because payload size, processed rate, retention needs,
and available database capacity differ between applications.

The package registers independent processing and cleanup hosted services. The
processing service:

- registers an `IHostedService`
- creates a scope per processing iteration
- calls `ITinyOutboxProcessor.ProcessPendingAsync`
- logs processing-iteration failures
- continues polling after non-cancellation processing failures
- waits `PollingInterval`
- stops claiming new work when cancellation is requested

The cleanup service removes only processed messages older than
`ProcessedRetention`, in batches no larger than `CleanupBatchSize`. Cleanup
failures do not stop processing. See [Retention and Cleanup](retention-and-cleanup.md)
for the exact boundary, provider concurrency behavior, and storage guidance.

`AddTinyEventsWorker(...)` also configures the core worker options used by `ITinyOutboxProcessor`, including `WorkerId`, `BatchSize`, and `ClaimTimeout`.

On shutdown, TinyEvents does not scan and release claims. If processing does not complete, claims expire naturally.

A hosted worker can remain running while processing iterations repeatedly fail, for example during a database outage. Treat worker logs and host-level health checks as part of production operations.

## Startup Validation

Before polling, the hosted worker resolves the processing graph once in a
temporary scope. This validates the processor dependencies and generated
dispatcher registrations without claiming or processing messages.

A startup validation failure escapes to the host. It is not logged, counted,
or retried as a processing-iteration failure because changing the application
configuration or registrations is required to recover.

After validation succeeds, operational iteration failures are logged and the
worker continues polling. Consumer failures, deserialization failures, unknown
event types stored in messages, and lease loss remain message-level outcomes
handled by the processor.

Database schema initialization is separate from runtime graph validation.
TinyEvents does not currently run schema migrations as part of worker startup.

## Runtime Logging

TinyEvents uses `Microsoft.Extensions.Logging`. The application owns log
providers, storage, formatting, alerting, and retention.

Runtime events have stable identifiers:

| Event ID | Name | Level | Meaning |
|---:|---|---|---|
| 1100 | `WorkerIterationFailed` | Warning | An iteration failed and the worker will retry. |
| 1101 | `WorkerRecovered` | Information | An iteration succeeded after one or more consecutive failures. |
| 1104 | `RepeatedWorkerFailures` | Error | A repeated-failure threshold was reached and operator attention is required. |
| 1110 | `CleanupBatchDeleted` | Debug | A bounded processed-message batch was deleted. |
| 1111 | `CleanupIterationFailed` | Warning | A cleanup iteration failed and will be retried. |
| 1112 | `RepeatedCleanupFailures` | Error | Cleanup reached a repeated-failure threshold. |
| 1113 | `CleanupRecovered` | Information | Cleanup succeeded after one or more failures. |
| 1202 | `EventProcessingFailed` | Warning | A message failed and another attempt is scheduled. |
| 1204 | `EventRetriesExhausted` | Error | A message reached `MaxAttempts` and no retry remains. |
| 1300 | `LeaseLost` | Warning | The worker no longer owns the message lease. |

Worker iteration failures are reported without stopping the worker:

- failures 1 through 4 emit `WorkerIterationFailed`
- failures 5, 10, 20, and 50 emit `RepeatedWorkerFailures`
- later multiples of 100 emit `RepeatedWorkerFailures`
- failures between those thresholds are not logged
- the first later success emits one `WorkerRecovered` event and resets the
  consecutive-failure count

Requested cancellation is not counted as a failure and does not emit a
failure event.

Runtime logs use structured properties where applicable:

- `MessageId`
- `EventType`
- `WorkerId`
- `Attempt`
- `MaximumAttempts`
- `NextAttemptAtUtc`
- `ConsecutiveFailures`
- `Operation`

TinyEvents does not log event payloads, serialized event bodies, connection
strings, credentials, secrets, tokens, or arbitrary event property values.

## Marking Processed Or Failed

Providers mark messages only when:

- `Id` matches
- `ClaimedBy` matches the current worker id
- `Status = Processing`

This prevents worker A from marking worker B's work.

If the mark operation affects no rows, TinyEvents treats that as a lost lease. The processor does not record a failed attempt for that message and continues with the next claimed message.

## Retries And Attempts

TinyEvents increments `AttemptCount` only when processing fails and the processor records that failure through the outbox store.

When processing fails before `MaxAttempts` is reached:

- `AttemptCount` is incremented
- `LastError` stores the failure message
- `Status` returns to `Pending`
- `NextAttemptAtUtc` is set to the current time plus `RetryDelay`

When processing fails and the next attempt would reach `MaxAttempts`:

- `AttemptCount` is incremented
- `LastError` stores the failure message
- `Status` becomes `Failed`
- `NextAttemptAtUtc` is cleared

Cancellation requested through the worker cancellation token is not recorded as a failed attempt.

Lost leases are not recorded as failed attempts. Another worker may already own the message or may reclaim it after the current lease expires.

An unknown event type is treated as a processing failure. It follows the same attempt and retry rules as a consumer failure.

## Unknown Event Types

TinyEvents stores the event type name in each outbox row. At processing time, that name must match a generated `ITinyEventDispatcher` registration in the current service provider.

If no dispatcher is registered for the stored event type, TinyEvents records the message as a processing failure. The message follows the normal retry and max-attempt rules.

Common causes are:

- the assembly containing the consumer was not loaded before TinyEvents registration
- TinyEvents registration ran before generated contributions were available
- an old outbox row references an event type that the application no longer handles
- a row was inserted manually with an invalid event type name

If the missing dispatcher is caused by startup or registration order, the message can succeed on a later attempt after the application is corrected. If the application will never handle that event type, the message eventually reaches `Failed`.

## Claim Timeout

TinyEvents v1 does not implement claim renewal or heartbeat.

`ClaimTimeout` starts when the batch is claimed. Messages in that batch are
processed sequentially, so the lease must cover the worst-case time for the
complete batch, including every consumer and completion update:

```csharp
services.AddTinyEventsWorker(options =>
{
    options.ClaimTimeout = TimeSpan.FromMinutes(10);
});
```

If the batch remains active beyond `ClaimTimeout`, another worker may reclaim a
later message before the original worker reaches or completes it. Increase the
timeout or reduce `BatchSize` when the complete worst-case batch cannot finish
inside the lease.

## Multiple Consumers And Retry

TinyEvents records completion for the event, not independently for each
consumer. Consumers execute sequentially. If an earlier consumer succeeds and a
later consumer fails, the message remains incomplete and its next attempt
invokes every consumer again. Consumer order is not a public coordination
contract; each consumer must tolerate repeated invocation.

## Delivery Guarantee

TinyEvents provides:

- at-least-once delivery
- database-backed multi-worker claiming
- no normal concurrent processing of the same unexpired message

TinyEvents does not provide:

- exactly-once side effects
- global duplicate prevention after crash/retry scenarios
- per-consumer durable completion checkpoints

Consumers must be idempotent.
