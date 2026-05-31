using Microsoft.EntityFrameworkCore;
using TinyEvents;
using TinyEvents.SqlServer.EntityFrameworkCore;
using TinyEvents.Sample.EfCore;

var connectionString = SampleSettings.GetConnectionString(args);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WelcomeEmailLog>();
builder.Services.AddScoped<UserRegistrationUseCase>();
builder.Services.AddDbContext<SampleDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});
builder.Services.UseSqlServerEntityFrameworkCoreOutbox<SampleDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.MapPost("/users", async (
    RegisterUserRequest request,
    UserRegistrationUseCase users,
    CancellationToken cancellationToken) =>
{
    var result = await users.RegisterAsync(request.Email, cancellationToken);
    return Results.Created($"/users/{result.UserId}", result);
});

app.MapPost("/outbox/process", async (
    ITinyOutboxProcessor processor,
    CancellationToken cancellationToken) =>
{
    await processor.ProcessPendingAsync(cancellationToken);
    return Results.Accepted();
});

app.MapGet("/welcome-emails", (WelcomeEmailLog log) =>
{
    return Results.Ok(new { log.Count, Emails = log.Snapshot() });
});

await app.RunAsync();

namespace TinyEvents.Sample.EfCore
{
    public sealed class UserRegistrationUseCase
    {
        private readonly SampleDbContext dbContext;
        private readonly ITinyEventPublisher events;

        public UserRegistrationUseCase(
            SampleDbContext dbContext,
            ITinyEventPublisher events)
        {
            this.dbContext = dbContext;
            this.events = events;
        }

        public async ValueTask<RegisterUserResult> RegisterAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            var userId = Guid.NewGuid();

            dbContext.Users.Add(new UserRow
            {
                Id = userId,
                Email = email
            });

            await events.PublishAsync(new UserCreated(userId, email), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new RegisterUserResult(userId, email);
        }
    }

    public sealed class SampleDbContext : DbContext
    {
        public SampleDbContext(DbContextOptions<SampleDbContext> options)
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
                entity.Property(user => user.Email).IsRequired().HasMaxLength(320);
            });

            modelBuilder.UseTinyEventsOutbox();
        }
    }

    public sealed class SendWelcomeEmail : IEventConsumer<UserCreated>
    {
        private readonly WelcomeEmailLog log;

        public SendWelcomeEmail(WelcomeEmailLog log)
        {
            this.log = log;
        }

        public ValueTask ConsumeAsync(
            UserCreated @event,
            CancellationToken cancellationToken)
        {
            log.Record(@event.Email);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class UserRow
    {
        public Guid Id { get; set; }

        public string Email { get; set; } = string.Empty;
    }

    public sealed class WelcomeEmailLog
    {
        private readonly List<string> emails = new List<string>();

        public int Count => emails.Count;

        public IReadOnlyList<string> Snapshot()
        {
            return emails.ToArray();
        }

        public void Record(string email)
        {
            emails.Add(email);
        }
    }

    public sealed record RegisterUserRequest(string Email);

    public sealed record RegisterUserResult(Guid UserId, string Email);

    public sealed record UserCreated(Guid UserId, string Email);

    internal static class SampleSettings
    {
        private const string EnvironmentVariable = "TINYEVENTS_SAMPLE_SQLSERVER";

        public static string GetConnectionString(string[] args)
        {
            if (args.Length > 0)
            {
                return args[0];
            }

            var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            throw new InvalidOperationException(
                $"Pass a SQL Server connection string as the first argument or set {EnvironmentVariable}.");
        }
    }
}
