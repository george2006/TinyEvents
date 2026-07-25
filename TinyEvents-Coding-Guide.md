# TinyEvents Coding Guide

You are working on **TinyEvents**, a production-grade .NET transactional outbox library being prepared for its first stable `1.0.0` release.

Treat this repository as the foundation of a small, high-quality software company. The objective is not merely to make the implementation correct. The code must be simple to understand, predictable to operate, difficult to misuse, and pleasant for experienced engineers to maintain.

## Product standard

TinyEvents should feel:

- Small.
- Explicit.
- Predictable.
- Boring in the best possible way.
- Easy to learn from the public API.
- Easy to debug in production.
- Honest about its guarantees and limitations.
- Consistent across providers and integration styles.

Do not optimise for showing technical cleverness. Optimise for clarity and long-term supportability.

## Core engineering principles

### Prefer obvious code

Choose the implementation that a competent engineer can understand immediately.

Do not introduce:

- Clever abstractions.
- Dense expressions.
- Hidden control flow.
- Unnecessary indirection.
- Generic frameworks created for one use case.
- Reflection when explicit registration or generated code is available.
- "Reusable" helpers that make the calling code harder to understand.
- Patterns added only because they are fashionable.

A small amount of duplication is acceptable when it keeps behaviour local and obvious.

Do not remove meaningful duplication until the shared concept is genuinely stable and clearly named.

### Use intention-revealing names

Names must explain the business or runtime meaning.

Prefer:

- `claimedMessages`
- `leaseExpiresAtUtc`
- `processingAttempt`
- `consumerType`
- `retryDelay`
- `affectedRows`

Avoid vague names such as:

- `data`
- `item`
- `result`
- `manager`
- `helper`
- `handler`
- `processor` when the specific responsibility can be named
- `DoWork`
- `Execute`
- `Process` without enough context

Boolean names should read naturally in a condition:

```csharp
if (leaseHasExpired)
if (messageCanBeRetried)
if (consumerWasResolved)
```

Avoid inverted or confusing boolean names.

### Make conditions readable

Complex conditions must be decomposed into named concepts.

Avoid:

```csharp
if (message.Attempts < options.MaxAttempts &&
    (message.LeaseUntilUtc == null || message.LeaseUntilUtc < now) &&
    !string.IsNullOrWhiteSpace(message.Type))
```

Prefer named variables or small focused methods:

```csharp
var leaseHasExpired = message.LeaseUntilUtc is null ||
                      message.LeaseUntilUtc < now;

var messageCanBeRetried = message.Attempts < options.MaxAttempts;

if (leaseHasExpired && messageCanBeRetried)
{
    ...
}
```

Do not extract trivial conditions merely to increase the number of methods. Extract them when the resulting name adds meaning.

Use early returns to avoid deep nesting.

### Keep methods focused

A method should perform one understandable operation.

Split methods when they independently:

- Load data.
- Decide policy.
- Mutate state.
- Call user code.
- Persist a result.
- Translate an exception.
- Log an outcome.

Do not split methods into tiny fragments that force the reader to jump between files to understand a basic flow.

The complete happy path should remain easy to follow.

### Be explicit about failure

Never silently ignore a failure unless that behaviour is an intentional and documented contract.

For important persistence operations:

- Check affected row counts where ownership or concurrency matters.
- Distinguish cancellation from failure.
- Preserve useful exception context.
- Do not catch `Exception` merely to log and continue without a clear policy.
- Do not convert programming errors into retries.
- Do not hide provider failures behind generic success results.
- Do not let a failed lease update look like successful ownership.

Failure behaviour must be visible through code, logs, exceptions, or durable state.

### Be precise about cancellation

Cancellation is not an operational failure.

Follow these rules:

- Pass cancellation tokens through all asynchronous calls that support them.
- Do not wrap `OperationCanceledException` as a processing error when cancellation was requested.
- Do not record cancellation as a failed message attempt unless the documented product behaviour explicitly requires it.
- Hosted workers must stop promptly and predictably.
- Cancellation checks should be placed at meaningful boundaries, not scattered arbitrarily.

### Be honest about delivery semantics

TinyEvents provides **at-least-once processing**, not exactly-once processing.

Do not add wording or implementation that implies exactly-once delivery.

Assume:

- A consumer may run more than once.
- A process may stop after the consumer succeeds but before completion is persisted.
- Leases may expire.
- Another worker may retry an event.
- Consumers must be idempotent where duplicate effects matter.

Documentation, naming, tests, and logs must reflect these realities.

### Keep configuration predictable

Configuration must not depend on service-registration order unless the behaviour is explicitly designed, documented, and tested.

Rules:

- A registration method must not silently replace unrelated configuration.
- Multiple configuration calls must have understandable composition semantics.
- Defaults must exist in one obvious place.
- Invalid options should fail early with useful messages.
- Configuration validation should preferably happen during application startup.
- Worker options and core processing options must not have ambiguous ownership.
- Avoid maintaining the same setting in two option classes unless there is an unavoidable boundary.

A user must not need to understand internal dependency-injection implementation details to configure TinyEvents safely.

### Protect the public API

Every public type becomes a long-term support commitment.

Before adding or retaining a public type, ask:

1. Does an application developer need to reference it?
2. Is it part of a deliberate extension point?
3. Can it be internal without blocking legitimate use?
4. Is its name suitable for years of public support?
5. Does it expose implementation details that may need to change?

Do not casually rename existing public APIs during hardening.

When a public API change is proposed:

- Explain why it is necessary before implementing it.
- Identify the migration impact.
- Prefer additive changes where reasonable.
- Do not create compatibility aliases unless the value outweighs the permanent API cost.

### Keep provider behaviour symmetrical

SQL Server and PostgreSQL implementations, and EF Core and ADO.NET integrations, must expose equivalent observable behaviour wherever the product contract is the same.

Review parity for:

- Claiming.
- Lease ownership.
- Retry eligibility.
- Attempt counting.
- Completion.
- Failure recording.
- Affected-row validation.
- Date and time handling.
- Cancellation.
- Table-name validation.
- Transaction behaviour.
- Serialization.
- Empty batches.
- Unknown event types.

Provider-specific SQL may differ. Product semantics should not drift accidentally.

### Use UTC consistently

All persisted and compared timestamps must have explicit UTC semantics.

Prefer names ending in `Utc` for relevant values.

Do not mix:

- Local time.
- Unspecified `DateTime`.
- Database server local time.
- Application UTC time.

Use one clear clock abstraction if the repository already has one. Do not introduce a clock abstraction merely for fashion; introduce it only where deterministic tests or consistent semantics require it.

### Logging should help operations

Logs must answer:

- Which event?
- Which event type?
- Which worker?
- Which attempt?
- What outcome?
- What will happen next?

Do not log event payloads by default because they may contain sensitive application data.

Avoid noisy logs inside tight polling loops.

Use appropriate levels:

- `Debug` or `Trace` for routine polling details.
- `Information` for meaningful lifecycle events.
- `Warning` for recoverable abnormal conditions.
- `Error` for failures that need operational attention.

Do not log the same exception repeatedly at multiple layers without additional value.

### Write contract-focused tests

Tests should describe externally meaningful behaviour.

Prefer test names such as:

```csharp
AddTinyEventsWorker_preserves_core_retry_configuration
ClaimAsync_returns_only_messages_owned_by_the_current_worker
ProcessAsync_does_not_record_cancellation_as_a_failure
CompleteAsync_fails_when_the_worker_no_longer_owns_the_lease
```

Avoid tests tightly coupled to private method structure.

Every bug fix must include a test that fails before the fix and passes after it.

For provider behaviour, prefer shared contract tests when they remain readable. Do not create a complicated test framework merely to eliminate duplicated test cases.

Tests should cover:

- Happy paths.
- Boundary values.
- Invalid configuration.
- Cancellation.
- Ownership loss.
- Concurrent claims.
- Temporary persistence failures.
- Permanent consumer failures.
- Retry exhaustion.
- Unknown event types.
- Empty batches.
- Multiple consumers where supported.
- Registration-order permutations.
- Provider parity.

### Comments explain why

Do not comment obvious code.

Bad:

```csharp
// Increment attempt count
attemptCount++;
```

Useful:

```csharp
// The attempt is persisted before invoking user code so a process crash
// cannot cause unlimited retries without advancing the counter.
```

Comments should explain constraints, guarantees, database behaviour, or non-obvious trade-offs.

Delete stale comments instead of preserving misleading history.

### Avoid speculative features

This is a hardening pass, not a feature-expansion project.

Do not add:

- New storage providers.
- New transport abstractions.
- Dashboards.
- Metrics frameworks.
- Dead-letter management APIs.
- Complex retry strategies.
- Distributed tracing frameworks.
- New serialization formats.
- Broad extensibility points.

A new feature is allowed only when it is required to make the existing advertised behaviour safe, coherent, or supportable for v1.0.

### Respect repository style

Before changing code:

- Inspect neighbouring production code.
- Inspect relevant tests.
- Inspect documentation describing the behaviour.
- Follow established formatting and language-version conventions.
- Reuse existing abstractions when they are clear and appropriate.

Do not run broad automatic formatting across unrelated files.

Do not modify generated files, `bin`, `obj`, package artifacts, or unrelated repository content.

## Working in small reviewable slices

Work on exactly **one slice at a time**.

A slice should normally:

- Address one behaviour or one closely related defect.
- Modify a small number of production files.
- Add or update focused tests.
- Avoid unrelated renaming or cleanup.
- Be understandable as a single commit.
- Be reviewable in approximately 10-20 minutes.

Do not begin the next slice until the current slice has been presented and explicitly approved.

Do not combine:

- Configuration changes with worker-loop restructuring.
- Public API changes with provider SQL changes.
- Naming cleanup with behavioural fixes.
- Documentation rewrites with unrelated implementation changes.
- Multiple provider fixes unless they implement the same contract and are best reviewed together.

If you discover another issue while implementing a slice, record it under **Follow-up findings**. Do not fix it opportunistically unless it directly blocks the current slice.

## Required workflow for every slice

### 1. Inspect

Before editing, inspect:

- Relevant implementation files.
- Relevant tests.
- Relevant public APIs.
- Relevant documentation.
- Equivalent code in other providers where parity matters.

### 2. State the slice

Before changing code, provide:

- **Problem**
- **Why it matters for v1.0**
- **Proposed behaviour**
- **Files expected to change**
- **Tests to add or update**
- **Explicit non-goals**

Do not edit until this scope is clear.

### 3. Implement minimally

Make the smallest coherent change that establishes the intended contract.

Do not redesign adjacent code unless necessary.

Do not add abstraction layers pre-emptively.

### 4. Validate

Run the narrowest relevant tests first.

Then run broader tests only when appropriate.

At minimum, report:

- Build command.
- Test command.
- Test result.
- Any tests not run.
- Any environment limitations.
- Any warnings introduced.

Never claim a command succeeded unless it was actually executed successfully.

### 5. Present the result

After the slice, stop and report:

#### Summary

A brief explanation of the behaviour changed.

#### Files changed

List each changed file and why.

#### Contract established

State the precise behaviour now guaranteed.

#### Tests

List tests added or modified and the commands executed.

#### Review notes

Call out any decision that deserves human attention.

#### Follow-up findings

List newly discovered issues without implementing them.

#### Suggested next slice

Recommend only one next slice.

Then wait for approval.

## Change discipline

Do not:

- Commit.
- Push.
- Open a pull request.
- Change package versions.
- Publish packages.
- Modify release tags.
- Rewrite large documentation sections.
- Delete compatibility APIs.
- Apply repository-wide formatting.

Unless explicitly requested.

Do not use `git reset --hard`, destructive checkout commands, or commands that discard local work.

Before editing, inspect `git status`.

After editing, show the relevant diff.

Preserve existing user changes.

## Definition of done for a slice

A slice is complete only when:

- The scope remained focused.
- Production behaviour is clear.
- Tests establish the contract.
- Relevant tests pass.
- No unrelated files changed.
- Public API impact is identified.
- Provider parity was considered.
- Documentation impact was considered.
- The resulting code is simpler or safer than before.
- The result has been presented for review.
- Work has stopped pending approval.

## Definition of done for TinyEvents 1.0

The repository is ready for `1.0.0` only when:

- Configuration behaviour is deterministic.
- Options are validated early.
- Worker lifecycle and cancellation are robust.
- Temporary infrastructure failures have an explicit policy.
- Claim ownership is verified.
- Lease loss cannot be mistaken for successful completion.
- Retry and attempt semantics are documented and tested.
- Unknown event types have explicit behaviour.
- At-least-once guarantees are documented clearly.
- Provider implementations behave consistently.
- Transaction requirements are clear.
- Public APIs have been deliberately reviewed.
- Packages contain only intended assets.
- Package metadata and versioning are centralised.
- Source Link and symbols are correct.
- Package-consumer smoke tests pass.
- Integration tests pass against supported databases.
- Documentation examples compile or are otherwise validated.
- The repository is clean of tracked build artifacts.
- The release process can be reproduced from a clean checkout.

## Release-readiness rule

Completing the planned slices does not automatically make TinyEvents ready for `1.0.0`.

After all hardening slices, perform a dedicated release-candidate audit from a clean checkout.

During the first pass of that audit:

- Do not edit any files.
- Build the complete solution.
- Run all unit tests.
- Run all supported database integration tests.
- Pack every published NuGet package.
- Inspect package contents and metadata.
- Run package-consumer smoke tests against the locally produced packages.
- Review the public API surface.
- Review documentation against actual behaviour.
- Review tracked repository files for build artifacts, secrets or accidental content.
- Review the release workflow and version source.
- List every remaining issue as either:
  - Release blocker.
  - Important follow-up after 1.0.
  - Acceptable known limitation.

TinyEvents may be declared ready for `1.0.0` only when:

- There are no unresolved release blockers.
- All required commands have actually passed.
- Any skipped validation is explicitly identified and manually accepted.
- The final package contents have been inspected.
- The documented guarantees match the implementation.
- The repository is clean.
- A human reviewer explicitly approves the release candidate.

Never claim that TinyEvents is ready to ship based only on static code inspection.
