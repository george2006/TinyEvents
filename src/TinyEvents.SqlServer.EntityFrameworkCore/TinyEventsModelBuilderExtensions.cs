using Microsoft.EntityFrameworkCore;

namespace TinyEvents.SqlServer.EntityFrameworkCore;

public static class TinyEventsModelBuilderExtensions
{
    public static ModelBuilder UseTinyEventsOutbox(
        this ModelBuilder modelBuilder,
        string tableName = "TinyOutbox")
    {
        if (modelBuilder is null)
        {
            throw new ArgumentNullException(nameof(modelBuilder));
        }

        var parsedTableName = TinySqlServerEfCoreTableName.Parse(tableName);

        modelBuilder.Entity<TinyOutboxMessage>(entity =>
        {
            entity.ToTable(parsedTableName.Table, parsedTableName.Schema);
            entity.HasKey(message => message.Id);
            entity.Property(message => message.EventType).IsRequired();
            entity.Property(message => message.Payload).IsRequired();
            entity.Property(message => message.Status).IsRequired();
            entity.Property(message => message.CreatedAtUtc).IsRequired();

            entity.HasIndex(message => new
            {
                message.Status,
                message.NextAttemptAtUtc,
                message.CreatedAtUtc
            });

            entity.HasIndex(message => new
            {
                message.Status,
                message.ClaimExpiresAtUtc
            });

            entity.HasIndex(message => new
            {
                message.ClaimedBy,
                message.Status
            });
        });

        return modelBuilder;
    }
}
