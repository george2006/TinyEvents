using TinyEvents;
using TinyEvents.SqlServer.AdoNet;
using TinyEvents.Sample.AdoNet.Consumers;
using TinyEvents.Sample.AdoNet.Contracts;
using TinyEvents.Sample.AdoNet.Infrastructure;
using TinyEvents.Sample.AdoNet.UseCases;
using TinyEvents.Worker;

var connectionString = SampleSettings.GetConnectionString(args);

await SqlServerSchema.EnsureCreatedAsync(connectionString);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WelcomeEmailLog>();
builder.Services.AddScoped<SampleAdoNetTransaction>(_ =>
{
    var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
    connection.Open();
    var transaction = connection.BeginTransaction();
    return new SampleAdoNetTransaction(connection, transaction);
});
builder.Services.AddScoped<RegisterUserUseCase>();

builder.Services.UseSqlServerAdoNetOutbox(options =>
{
    options.UseCurrentTransaction(provider =>
    {
        // In this sample we create a small scoped transaction object to keep
        // the app self-contained. In a real application, map this to your
        // existing Unit of Work, DbSession, repository transaction, or directly
        // registered DbConnection/DbTransaction.
        var current = provider.GetRequiredService<SampleAdoNetTransaction>();
        return new TinyAdoNetTransactionContext(current.Connection, current.Transaction);
    });

    // No application connection factory yet? Open SqlConnection directly here.
    // If your app already owns a factory, resolve it from provider and call it instead.
    options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
    {
        var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    });
});
builder.Services.AddTinyEventsWorker(options =>
{
    options.CleanupEnabled = true;
    options.ProcessedRetention = TimeSpan.FromHours(1);
    options.CleanupBatchSize = 1_000;
    options.CleanupInterval = TimeSpan.FromSeconds(1);
});

var app = builder.Build();

await app.Services.MigrateTinyEventsAsync();

app.MapPost("/users", async (
    RegisterUserRequest request,
    RegisterUserUseCase users,
    CancellationToken cancellationToken) =>
{
    var result = await users.RegisterAsync(request.Email, cancellationToken);
    return Results.Created($"/users/{result.UserId}", result);
});

app.MapGet("/welcome-emails", (WelcomeEmailLog log) =>
{
    return Results.Ok(new { log.Count, Emails = log.Snapshot() });
});

await app.RunAsync();
