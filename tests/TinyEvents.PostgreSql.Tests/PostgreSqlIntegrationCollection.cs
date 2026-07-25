using Xunit;

namespace TinyEvents.PostgreSql.Tests;

[CollectionDefinition(Name)]
public sealed class PostgreSqlIntegrationCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}
