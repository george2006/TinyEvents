using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TinyEvents.PostgreSql.EntityFrameworkCore;
using Xunit;

namespace TinyEvents.PostgreSql.Tests;

public sealed class EfCorePostgreSqlWriterRuntimeTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture fixture;

    public EfCorePostgreSqlWriterRuntimeTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Writer_commits_business_data_and_outbox_message_with_save_changes()
    {
        await fixture.ResetSchemaAsync();
        var services = BuildServices();
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var userId = Guid.NewGuid();

        dbContext.Users.Add(new UserRow
        {
            Id = userId,
            Email = "user@example.com"
        });

        await events.PublishAsync(new UserCreated(userId, "user@example.com"));
        await dbContext.SaveChangesAsync();

        Assert.Equal(1, await dbContext.Users.CountAsync());
        Assert.Equal(1, await dbContext.Set<TinyOutboxMessage>().CountAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Writer_rolls_back_business_data_and_outbox_message_with_db_context_transaction()
    {
        await fixture.ResetSchemaAsync();
        var services = BuildServices();
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<ITinyEventPublisher>();
        var userId = Guid.NewGuid();

        await using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            dbContext.Users.Add(new UserRow
            {
                Id = userId,
                Email = "rollback@example.com"
            });

            await events.PublishAsync(new UserCreated(userId, "rollback@example.com"));
            await dbContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await CountAsync("\"Users\""));
        Assert.Equal(0, await CountAsync("\"TinyOutbox\""));
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString);
        });
        services.UseTinyEvents();
        services.Replace(ServiceDescriptor.Scoped<ITinyOutboxWriter, TinyPostgreSqlEfCoreOutboxWriter<TestDbContext>>());

        return services.BuildServiceProvider();
    }

    private async Task<int> CountAsync(string tableName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
            : base(options)
        {
        }

        public DbSet<UserRow> Users => Set<UserRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRow>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(user => user.Id);
                entity.Property(user => user.Email).IsRequired();
            });

            modelBuilder.UseTinyEventsOutbox();
        }
    }

    private sealed class UserRow
    {
        public Guid Id { get; set; }

        public string Email { get; set; } = string.Empty;
    }

    private sealed record UserCreated(Guid UserId, string Email);
}
