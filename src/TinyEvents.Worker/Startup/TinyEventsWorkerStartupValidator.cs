using Microsoft.Extensions.DependencyInjection;

namespace TinyEvents.Worker;

internal sealed class TinyEventsWorkerStartupValidator
{
    private readonly IServiceScopeFactory scopeFactory;

    public TinyEventsWorkerStartupValidator(IServiceScopeFactory scopeFactory)
    {
        this.scopeFactory = scopeFactory
            ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public void ValidateConfiguration()
    {
        using var scope = scopeFactory.CreateScope();

        _ = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
    }
}
