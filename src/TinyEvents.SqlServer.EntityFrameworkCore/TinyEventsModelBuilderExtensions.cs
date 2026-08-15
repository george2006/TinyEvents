using Microsoft.EntityFrameworkCore;
using TinyEvents.Migrations.SqlServer;

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
        var migrationTableIdentity = SqlServerMigrationTableIdentity.Parse(tableName);

        modelBuilder.Entity<TinyOutboxMessage>(entity =>
        {
            entity.ToTable(parsedTableName.Table, parsedTableName.Schema);
            entity.HasKey(message => message.Id)
                .HasName(migrationTableIdentity.OutboxPrimaryKey);
            entity.Property(message => message.EventType).IsRequired().HasMaxLength(512);
            entity.Property(message => message.Payload).IsRequired();
            entity.Property(message => message.Status).IsRequired();
            entity.Property(message => message.ClaimedBy).HasMaxLength(256);
            entity.Property(message => message.CreatedAtUtc).IsRequired();

            entity.HasIndex(message => new
            {
                message.Status,
                message.NextAttemptAtUtc,
                message.CreatedAtUtc
            }).HasDatabaseName(migrationTableIdentity.PendingIndex);

            entity.HasIndex(message => new
            {
                message.Status,
                message.ClaimExpiresAtUtc
            }).HasDatabaseName(migrationTableIdentity.ExpiredProcessingIndex);

            entity.HasIndex(message => new
            {
                message.ClaimedBy,
                message.Status
            }).HasDatabaseName(migrationTableIdentity.ClaimedByIndex);
        });

        return modelBuilder;
    }
}
