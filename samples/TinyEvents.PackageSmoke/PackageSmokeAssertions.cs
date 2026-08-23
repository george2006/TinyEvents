using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinyEvents.Worker;

namespace TinyEvents.PackageSmoke;

internal static class PackageSmokeAssertions
{
    public static void RequireCondition(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected package smoke test condition was not met.");
        }
    }

    public static void RequireService(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected package service registration was missing.");
        }
    }

    public static void RequireWorkerHostedServices(IServiceProvider provider)
    {
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var processingServices = hostedServices
            .OfType<TinyEventsBackgroundService>()
            .ToArray();

        RequireCondition(hostedServices.Length == 2);
        RequireCondition(processingServices.Length == 1);
    }
}
