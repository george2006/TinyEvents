using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace TinyEvents.SqlServer.Tests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
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
        if (!SqlServerIntegrationSettings.Enabled)
        {
            return;
        }

        await container.StartAsync();
        await ResetSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerIntegrationSettings.Enabled)
        {
            return;
        }

        await container.DisposeAsync();
    }

    public async Task ResetSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SqlServerSchema.CreateSchemaSql;
        await command.ExecuteNonQueryAsync();
    }
}
