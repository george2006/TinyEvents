using Xunit;

namespace TinyEvents.PostgreSql.Integration;

[CollectionDefinition(Name)]
public sealed class PostgreSqlIntegrationCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}
