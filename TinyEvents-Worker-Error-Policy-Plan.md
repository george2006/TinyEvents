# TinyEvents Worker Error Policy

This document defines the worker error-policy feature and its implementation
slices. The engineering and review rules live in
`TinyEvents-Coding-Guide.md`.

## Goal

Keep the worker available when an individual message or polling iteration
fails, while preserving correct outbox state and making degraded operation
visible.

The policy is based on where a failure occurs. It does not attempt to guess
whether arbitrary exception types are transient or fatal.

## Failure Boundaries

### Message Failure

A message failure occurs while preparing or invoking one claimed message:

- resolving its dispatcher
- deserializing its payload
- resolving or invoking its consumers
- retrieving a secret or calling an external service from a consumer
- executing consumer business logic

Message failures are recorded through `MarkFailedAsync` while the worker still
owns the lease. They follow the existing attempt, retry, and exhaustion rules.
The worker continues with the next claimed message.

Requested cancellation and lease loss are not message failures.

### Completion Failure

A completion failure occurs after every consumer has succeeded but the store
cannot mark the message as processed.

The processor must not call `MarkFailedAsync` after a completion failure. The
consumer may already have produced side effects, and the processed update may
have succeeded even if its response was lost.

A non-lease completion failure escapes the processor as an iteration failure.
Lease loss remains an expected at-least-once race: it is logged and processing
continues.

### Iteration Failure

An iteration failure prevents the worker from claiming or updating outbox
state:

- database or network failure during claim
- completion-store failure
- failure while persisting a genuine message failure
- dependency-injection scope or processor resolution failure

The hosted worker logs the failure, waits, and tries another iteration. It
does not infer fatality from provider-specific or user-defined exception
types.

Repeated failures must be observable without producing an unbounded stream of
identical high-severity logs. The first later successful iteration reports
recovery and resets the consecutive-failure count.

### Startup Failure

Only an explicit startup operation may fail the host before polling starts.
Current examples are invalid options or missing required registrations when
they are explicitly validated at startup.

Schema migration behavior is not part of this feature because built-in
migrations do not exist yet. The migration feature will define its startup
contract when implemented.

### Cancellation

Cancellation requested through the worker token:

- stops the worker cleanly
- starts no additional message work
- does not call `MarkFailedAsync`
- does not increment message attempts
- is not logged as an error

An unrelated `OperationCanceledException` thrown while the supplied token is
not canceled remains a message or iteration failure according to where it was
thrown.

### Lease Loss

Lease loss:

- does not increment attempts
- does not kill the worker
- does not cause another state transition
- emits a warning
- allows processing to continue

## Design Constraints

- Prefer structural failure boundaries over exception-type classification.
- Do not introduce provider-specific transient-error classifiers.
- Do not introduce schema or migration exceptions before those features exist.
- Do not add an arbitrary limit that kills the worker after repeated failures.
- Use the existing polling interval as the initial retry delay.
- Keep SQL Server, PostgreSQL, EF Core, and ADO.NET behavior symmetrical.
- Add no public API unless a later slice proves it is necessary.

## Implementation Slices

Each slice should change only a few files and be reviewable in five to ten
minutes. Stop for review after every slice.

### W1 — Separate Message And Completion Failures

Status: completed; awaiting review.

Files:

- `src/TinyEvents/Processing/TinyOutboxProcessor.cs`
- `tests/TinyEvents.Tests/Processing/TinyOutboxProcessorTests.cs`

Contract:

- Consumer, dispatcher, and deserialization failures call `MarkFailedAsync`.
- A successful consumer run proceeds to `MarkProcessedAsync`.
- A non-lease `MarkProcessedAsync` failure escapes the processor.
- `MarkFailedAsync` is never called because completion persistence failed.
- Existing cancellation and lease-loss behavior remains unchanged.

Non-goals:

- No worker-loop changes.
- No new exception types.
- No logging catalogue.
- No provider changes.

### W2 — Protect Failure-Persistence Boundaries

Status: completed; awaiting review.

Files:

- `src/TinyEvents/Processing/TinyOutboxProcessor.cs`
- `tests/TinyEvents.Tests/Processing/TinyOutboxProcessorTests.cs`

Contract:

- A genuine message failure is recorded once.
- A non-lease failure from `MarkFailedAsync` escapes as an iteration failure.
- Requested cancellation during failure persistence escapes unchanged.
- Lease loss during failure persistence remains a warning and does not retry
  the state transition.

### W3 — Complete Cancellation Contract Tests

Files:

- `tests/TinyEvents.Tests/Processing/TinyOutboxProcessorTests.cs`
- `tests/TinyEvents.Worker.Tests/TinyEventsWorkerTests.cs`

Contract tests:

- Cancellation during claim escapes.
- Cancellation after claim starts no message work.
- Cancellation during consumer execution is not recorded as failure.
- Cancellation during failure persistence escapes.
- Worker shutdown during an iteration stops promptly.
- Requested cancellation is not logged as an iteration failure.

Production changes are out of scope unless a test proves a defect.

### W4 — Inventory Worker Logging

Read-only.

Inspect current worker, processor, and lease-loss logs. Propose the smallest
stable event catalogue for:

- iteration failure
- prominent repeated failure
- recovery
- message failure
- lease loss

Do not implement logging in this slice.

### W5 — Add Consecutive Failure State

Files:

- `src/TinyEvents.Worker/TinyEventsBackgroundService.cs`
- `tests/TinyEvents.Worker.Tests/TinyEventsWorkerTests.cs`

Contract:

- Each failed iteration increments a consecutive-failure count.
- A successful iteration resets the count.
- Cancellation is not counted.
- The worker remains alive after iteration failures.

This slice establishes state only. Final structured logging belongs to W6.

### W6 — Report Degradation And Recovery

Files:

- one small internal logging catalogue file
- `src/TinyEvents.Worker/TinyEventsBackgroundService.cs`
- `tests/TinyEvents.Worker.Tests/TinyEventsWorkerTests.cs`

Initial policy:

- Failures one through four log Warning.
- Failure five logs Error.
- Later failures log only at meaningful deterministic thresholds.
- The first successful iteration after failures logs Information with the
  previous failure count.
- Recovery resets the count.

Exact later thresholds must be approved during W4.

### W7 — Document The Runtime Contract

Files:

- `docs/workers.md`

Document:

- message failures
- completion failures
- iteration retries
- cancellation
- lease loss
- polling delay after failure
- degraded-operation and recovery logs
- host-level health monitoring expectations

## Deferred Decisions

These require concrete later features or operational evidence:

- migration initialization failures
- schema compatibility failures
- provider-specific transient detection
- exponential backoff
- health-check APIs
- metrics and telemetry
