using Microsoft.Extensions.DependencyInjection;

namespace TinyEvents;

public static class TinyEventsBootstrap
{
    private static readonly object SyncRoot = new object();
    private static readonly List<ITinyEventsContribution> Contributions = new List<ITinyEventsContribution>();

    public static void AddContribution(ITinyEventsContribution contribution)
    {
        if (contribution is null)
        {
            throw new ArgumentNullException(nameof(contribution));
        }

        lock (SyncRoot)
        {
            if (HasContribution(contribution))
            {
                return;
            }

            Contributions.Add(contribution);
        }
    }

    public static void Apply(IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (HasAppliedMarker(services))
        {
            return;
        }

        services.AddSingleton<TinyEventsBootstrapAppliedMarker>();

        ITinyEventsContribution[] snapshot;

        lock (SyncRoot)
        {
            snapshot = Contributions.ToArray();
        }

        foreach (var contribution in snapshot)
        {
            contribution.Register(services);
        }
    }

    private static bool HasContribution(ITinyEventsContribution contribution)
    {
        foreach (var registered in Contributions)
        {
            if (ReferenceEquals(registered, contribution))
            {
                return true;
            }

            if (registered.GetType() == contribution.GetType())
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAppliedMarker(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(TinyEventsBootstrapAppliedMarker))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class TinyEventsBootstrapAppliedMarker
    {
    }
}

