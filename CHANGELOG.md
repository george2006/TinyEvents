# Changelog

## 1.0.0-beta.2 - 2026-10-15

- Added scoped `ITinyOutboxStatusReader` across all four database providers to observe outbox counts and activity timestamps without loading payloads or changing the schema. See [Outbox Status](docs/workers.md#outbox-status).
