using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TinyEvents;
using TinyEvents.PostgreSql.AdoNet;
using TinyEvents.SqlServer.AdoNet;

var sqlServerConnectionString = GetRequiredEnvironmentVariable(
    "TINYEVENTS_ALPHA_UPGRADE_SQLSERVER");
var postgreSqlConnectionString = GetRequiredEnvironmentVariable(
    "TINYEVENTS_ALPHA_UPGRADE_POSTGRESQL");

#if LEGACY_SCHEMA
await CreateSqlServerDatabaseAsync(sqlServerConnectionString);
await ExecuteSqlServerAsync(
    sqlServerConnectionString,
    TinySqlServerAdoNetSchema.CreateOutboxSql());

await CreatePostgreSqlDatabaseAsync(postgreSqlConnectionString);
await ExecutePostgreSqlAsync(
    postgreSqlConnectionString,
    TinyPostgreSqlAdoNetSchema.CreateOutboxSql());

Console.WriteLine("Published alpha packages created both legacy outbox schemas.");
#else
await MigrateSqlServerAsync(sqlServerConnectionString);
await MigratePostgreSqlAsync(postgreSqlConnectionString);

await RequireSingleHistoryRowAsync(
    new SqlConnection(sqlServerConnectionString),
    "SELECT COUNT(*) FROM dbo.TinyOutboxMigrations;");
await RequireSingleHistoryRowAsync(
    new NpgsqlConnection(postgreSqlConnectionString),
    """SELECT COUNT(*) FROM "TinyOutboxMigrations";""");

Console.WriteLine("Local packages baselined both published-alpha schemas.");
#endif

#if !LEGACY_SCHEMA
static async Task MigrateSqlServerAsync(string connectionString)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.UseSqlServerAdoNetOutbox(options =>
    {
        options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        });
    });

    await using var provider = services.BuildServiceProvider();
    await provider.MigrateTinyEventsAsync();
}

static async Task MigratePostgreSqlAsync(string connectionString)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.UsePostgreSqlAdoNetOutbox(options =>
    {
        options.UseWorkerConnectionFactory(async (_, cancellationToken) =>
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        });
    });

    await using var provider = services.BuildServiceProvider();
    await provider.MigrateTinyEventsAsync();
}
#endif

#if LEGACY_SCHEMA
static async Task CreateSqlServerDatabaseAsync(string connectionString)
{
    var builder = new SqlConnectionStringBuilder(connectionString);
    var databaseName = builder.InitialCatalog;
    builder.InitialCatalog = "master";

    var escapedLiteral = databaseName.Replace("'", "''");
    var escapedIdentifier = databaseName.Replace("]", "]]", StringComparison.Ordinal);

    await ExecuteSqlServerAsync(
        builder.ConnectionString,
        $"IF DB_ID(N'{escapedLiteral}') IS NULL CREATE DATABASE [{escapedIdentifier}];");
}

static async Task CreatePostgreSqlDatabaseAsync(string connectionString)
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString);
    var databaseName = builder.Database
        ?? throw new InvalidOperationException("PostgreSQL database name is required.");
    builder.Database = "postgres";

    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync();

    await using var exists = connection.CreateCommand();
    exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @DatabaseName;";
    exists.Parameters.AddWithValue("DatabaseName", databaseName);

    if (await exists.ExecuteScalarAsync() is not null)
    {
        return;
    }

    var escapedIdentifier = databaseName.Replace("\"", "\"\"", StringComparison.Ordinal);
    await using var create = connection.CreateCommand();
    create.CommandText = $"CREATE DATABASE \"{escapedIdentifier}\";";
    await create.ExecuteNonQueryAsync();
}

static Task ExecuteSqlServerAsync(string connectionString, string sql) =>
    ExecuteAsync(new SqlConnection(connectionString), sql);

static Task ExecutePostgreSqlAsync(string connectionString, string sql) =>
    ExecuteAsync(new NpgsqlConnection(connectionString), sql);

static async Task ExecuteAsync(DbConnection connection, string sql)
{
    await using (connection)
    {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
#endif

#if !LEGACY_SCHEMA
static async Task RequireSingleHistoryRowAsync(DbConnection connection, string sql)
{
    await using (connection)
    {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());

        if (count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one baselined migration history row but found {count}.");
        }
    }
}
#endif

static string GetRequiredEnvironmentVariable(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable '{name}' is required.");
