using Npgsql;
using Xunit;

namespace TinyEvents.PostgreSql.AdoNet.Tests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class PostgreSqlContainerTests
{
    private readonly PostgreSqlFixture fixture;

    public PostgreSqlContainerTests(PostgreSqlFixture fixture)
    {
        this.fixture = fixture;
    }

    [PostgreSqlIntegrationFact]
    public async Task Container_starts_and_accepts_connection()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";

        var result = await command.ExecuteScalarAsync();

        Assert.Equal(1, result);
    }
}
