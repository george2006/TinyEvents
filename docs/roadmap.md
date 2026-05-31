# Roadmap

TinyEvents is in alpha. The current goal is to keep the core model small while proving the runtime against SQL Server.

## Before 1.0

Planned hardening:

- polish README and provider docs as APIs settle
- keep SQL Server runtime tests green with Testcontainers
- add more sample documentation
- review NuGet package boundaries
- publish provider packages after the first alpha
- decide whether SQL Server providers should use explicit package names
- keep source generator diagnostics focused and useful

## Provider Boundaries

The first alpha prepares these package boundaries:

- `TinyEvents`
- `TinyEvents.SqlServer.EntityFrameworkCore`
- `TinyEvents.SqlServer.AdoNet`
- `TinyEvents.Worker`

Other databases should be separate provider packages once they exist.

## Not Planned For The Core

TinyEvents core should not grow into:

- a broker abstraction
- a saga or workflow engine
- a migration runner
- a distributed lock framework
- a general-purpose scheduler
- a direct in-process notification dispatcher

## Possible Future Packages

Future functionality should stay isolated when it brings external dependencies:

- migration helper packages
- host-specific packages
- additional database providers
- optional claim renewal support for long-running consumers

Claim renewal is not part of v1. For v1, `ClaimTimeout` should be configured longer than expected consumer processing time.
