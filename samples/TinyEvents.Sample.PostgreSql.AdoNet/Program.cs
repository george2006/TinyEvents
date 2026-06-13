using Npgsql;
using TinyEvents;
using TinyEvents.PostgreSql.AdoNet;
using TinyEvents.Sample.PostgreSql.AdoNet.Consumers;
using TinyEvents.Sample.PostgreSql.AdoNet.Contracts;
using TinyEvents.Sample.PostgreSql.AdoNet.Infrastructure;
using TinyEvents.Sample.PostgreSql.AdoNet.UseCases;

var connectionString = SampleSettings.GetConnectionString(args);

await PostgreSqlSchema.EnsureCreatedAsync(connectionString);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WelcomeEmailLog>();
builder.Services.AddScoped<SampleAdoNetTransaction>(_ =>
{
    var connection = new NpgsqlConnection(connectionString);
    connection.Open();
    var transaction = connection.BeginTransaction();
    return new SampleAdoNetTransaction(connection, transaction);
});
builder.Services.AddScoped<RegisterUserUseCase>();

builder.Services.UsePostgreSqlAdoNetOutbox(options =>
{
    options.UseCurrentTransaction(provider =>
    {
        // In this sample we create a small scoped transaction object to keep
        // the app self-contained. In a real application, map this to your
        // existing Unit of Work, DbSession, repository transaction, or directly
        // registered DbConnection/DbTransaction.
        var current = provider.GetRequiredService<SampleAdoNetTransaction>();
        return new TinyPostgreSqlAdoNetTransactionContext(current.Connection, current.Transaction);
    });

    // No application connection factory yet? Open NpgsqlConnection directly here.
    // If your app already owns a factory, resolve it from provider and call it instead.
    options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    });
});

var app = builder.Build();

app.MapPost("/users", async (
    RegisterUserRequest request,
    RegisterUserUseCase users,
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
