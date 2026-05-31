namespace TinyEvents.Sample.AdoNet.Events;

public sealed record UserCreated(Guid UserId, string Email);
