# TinyEvents.PostgreSql.AdoNet

PostgreSQL ADO.NET provider package for TinyEvents outbox storage and worker claiming.

This package boundary exists so PostgreSQL support can be built without changing TinyEvents core or the SQL Server providers.

Runtime behavior is being added in small reviewed slices. The provider will follow the same ownership model as the SQL Server ADO.NET provider:

- applications own publishing connections and transactions
- TinyEvents writes the outbox message inside the supplied transaction
- TinyEvents does not commit, roll back, open, or dispose the application transaction
- worker operations use provider-owned connections from a configured worker connection factory

PostgreSQL worker claiming will use `FOR UPDATE SKIP LOCKED`.

## More Documentation

- Project documentation: https://github.com/george2006/TinyEvents
- Worker guide: https://github.com/george2006/TinyEvents/blob/main/docs/workers.md
- Schema and migrations: https://github.com/george2006/TinyEvents/blob/main/docs/schema-and-migrations.md
