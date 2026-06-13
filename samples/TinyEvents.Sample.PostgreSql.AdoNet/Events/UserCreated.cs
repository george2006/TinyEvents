namespace TinyEvents.Sample.PostgreSql.AdoNet.Events;

public sealed record UserCreated(Guid UserId, string Email);
