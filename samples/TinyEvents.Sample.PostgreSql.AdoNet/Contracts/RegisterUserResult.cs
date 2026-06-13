namespace TinyEvents.Sample.PostgreSql.AdoNet.Contracts;

public sealed record RegisterUserResult(Guid UserId, string Email);
