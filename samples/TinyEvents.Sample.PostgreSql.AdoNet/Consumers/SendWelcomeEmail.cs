using TinyEvents.Sample.PostgreSql.AdoNet.Events;

namespace TinyEvents.Sample.PostgreSql.AdoNet.Consumers;

public sealed class SendWelcomeEmail : IEventConsumer<UserCreated>
{
    private readonly WelcomeEmailLog log;

    public SendWelcomeEmail(WelcomeEmailLog log)
    {
        this.log = log;
    }

    public ValueTask ConsumeAsync(
        UserCreated @event,
        CancellationToken cancellationToken)
    {
        log.Record(@event.Email);
        return ValueTask.CompletedTask;
    }
}
