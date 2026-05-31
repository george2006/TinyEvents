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
}
