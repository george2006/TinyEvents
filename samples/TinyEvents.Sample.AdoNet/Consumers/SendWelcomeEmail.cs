using TinyEvents.Sample.AdoNet.Events;

namespace TinyEvents.Sample.AdoNet.Consumers;

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
