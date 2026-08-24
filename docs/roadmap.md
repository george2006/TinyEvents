# TinyEvents Product Roadmap

This roadmap describes product direction without delivery dates. Scope may
change as each capability is designed and validated.

## V1 Evolution

### Failed Messages and Replay

Inspect terminally failed messages and replay one message or a bounded
selection after the underlying problem has been corrected. Replay preserves the
existing at-least-once delivery contract.

### Worker Registry

Maintain a durable view of the worker instances participating in event
processing, including their identity, version, capacity, and lifecycle state.

### Worker Heartbeats

Detect active, stale, and stopped workers through database-authoritative
liveness information.

### Processing Progress

Expose the work currently owned by each worker and support long-running
processing without unnecessary redelivery.

## V2 Evolution

### Durable Consumer Checkpoints

Record completion independently for every consumer attached to an event so a
later consumer failure does not require repeating consumers already completed.

### Duplicate-Resistant Processing

Reduce avoidable duplicate invocations through stronger ownership and durable
progress while keeping external side effects under an honest at-least-once
contract.
