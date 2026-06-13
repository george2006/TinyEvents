# EF Core Providers

TinyEvents EF Core providers store outbox messages through a caller-owned `DbContext`.

Publishing adds an outbox entity to the scoped `DbContext`. TinyEvents does not call `SaveChangesAsync`; the caller owns the save boundary.

Choose the provider for your database:

- [SQL Server EF Core](sql-server/ef-core.md)
- [PostgreSQL EF Core](postgresql/ef-core.md)

Both providers use the same core publishing model:

```csharp
dbContext.Users.Add(user);
await events.PublishAsync(new UserCreated(user.Id, user.Email), ct);
await dbContext.SaveChangesAsync(ct);
```

Worker claiming is database-specific:

- SQL Server uses SQL Server locking hints and atomic update/output SQL.
- PostgreSQL uses `FOR UPDATE SKIP LOCKED` inside an atomic update/returning SQL statement.
