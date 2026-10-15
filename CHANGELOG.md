# Changelog

## Unreleased

- Added scoped `ITinyOutboxStatusReader` across all four database providers to observe outbox counts and activity timestamps without loading payloads or changing the schema. See [Outbox Status](docs/workers.md#outbox-status).
