using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace TinyEvents.PostgreSql.Tests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tinyevents")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString
    {
        get
        {
            return container.GetConnectionString();
        }
    }

    public async Task InitializeAsync()
    {
        if (!PostgreSqlIntegrationSettings.Enabled)
        {
            return;
        }

        await container.StartAsync();
        await ResetSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        if (!PostgreSqlIntegrationSettings.Enabled)
        {
            return;
        }

        await container.DisposeAsync();
    }

    public async Task ResetSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = PostgreSqlSchema.CreateSchemaSql;
        await command.ExecuteNonQueryAsync();
    }
}
