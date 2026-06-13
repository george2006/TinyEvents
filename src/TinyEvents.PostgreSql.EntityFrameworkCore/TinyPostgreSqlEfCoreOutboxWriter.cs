using Microsoft.EntityFrameworkCore;

namespace TinyEvents.PostgreSql.EntityFrameworkCore;

public sealed class TinyPostgreSqlEfCoreOutboxWriter<TDbContext> : ITinyOutboxWriter
    where TDbContext : DbContext
{
    private readonly TDbContext dbContext;

    public TinyPostgreSqlEfCoreOutboxWriter(TDbContext dbContext)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        this.dbContext = dbContext;
    }

    public ValueTask AddAsync(
        TinyOutboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        cancellationToken.ThrowIfCancellationRequested();
        dbContext.Set<TinyOutboxMessage>().Add(message);
        return ValueTask.CompletedTask;
    }
}
