using Microsoft.Extensions.DependencyInjection;

namespace TinyEvents;

public interface ITinyEventsContribution
{
    void Register(IServiceCollection services);
}

