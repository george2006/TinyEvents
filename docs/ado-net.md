# ADO.NET Providers

TinyEvents ADO.NET providers store outbox messages through application-owned ADO.NET transactions.

TinyEvents joins the transaction supplied by the application. It does not open the publishing connection, begin the transaction, commit, roll back, or dispose the application transaction.

Choose the provider for your database:

- [SQL Server ADO.NET](sql-server/ado-net.md)
- [PostgreSQL ADO.NET](postgresql/ado-net.md)

The application owns the persistence boundary:

```csharp
await InsertUserAsync(connection, transaction, user, ct);
await events.PublishAsync(new UserCreated(user.Id, user.Email), ct);
await transaction.CommitAsync(ct);
```

Worker operations use a separate provider-configured connection factory. Connections returned by the worker factory are owned by TinyEvents for that worker operation.
