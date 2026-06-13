namespace TinyEvents.PostgreSql.EntityFrameworkCore;

public sealed class TinyEventsPostgreSqlEntityFrameworkCoreOptions
{
    public string TableName { get; set; } = "TinyOutbox";
}
