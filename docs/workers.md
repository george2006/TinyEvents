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

Install:

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.1
```

The hosted worker integration is included in the alpha package. It may move to a dedicated package after the first alpha.

Register:

```csharp
using TinyEvents.Worker;

services.AddTinyEventsWorker(options =>
{
    options.BatchSize = 50;
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.ClaimTimeout = TimeSpan.FromMinutes(5);
});
```

The hosted worker:

- creates a scope per processing iteration
- calls `ITinyOutboxProcessor.ProcessPendingAsync`
- waits `PollingInterval`
- stops claiming new work when cancellation is requested

On shutdown, TinyEvents does not scan and release claims. If processing does not complete, claims expire naturally.

## Marking Processed Or Failed

Providers mark messages only when:

- `Id` matches
- `ClaimedBy` matches the current worker id
- `Status = Processing`

This prevents worker A from marking worker B's work.

## Claim Timeout

TinyEvents v1 does not implement claim renewal or heartbeat.

Set `ClaimTimeout` longer than the expected maximum consumer processing time:

```csharp
services.AddTinyEventsWorker(options =>
{
    options.ClaimTimeout = TimeSpan.FromMinutes(10);
});
```

If a consumer runs longer than `ClaimTimeout`, another worker may reclaim and process the same message.

## Delivery Guarantee

TinyEvents provides:

- at-least-once delivery
- database-backed multi-worker claiming
- no normal concurrent processing of the same unexpired message

TinyEvents does not provide:

- exactly-once side effects
- global duplicate prevention after crash/retry scenarios

Consumers must be idempotent.
