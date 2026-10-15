using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TinyEvents.SqlServer.AdoNet.Tests;

public sealed class TinySqlServerAdoNetStatusTests
{
    [Fact]
    public void Reader_is_scoped_and_resolution_does_not_open_a_connection()
    {
        var services = new ServiceCollection();
        services.UseSqlServerAdoNetOutbox(options =>
            options.UseWorkerConnectionFactory((_, _) => throw new InvalidOperationException("Unexpected connection.")));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var reader = firstScope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();

        Assert.IsType<TinySqlServerAdoNetOutboxStore>(reader);
        Assert.Same(reader, firstScope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>());
        Assert.NotSame(reader, secondScope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>());
    }

    [Fact]
    public async Task Cancelled_observation_does_not_open_a_connection()
    {
        var services = new ServiceCollection();
        services.UseSqlServerAdoNetOutbox(options =>
            options.UseWorkerConnectionFactory((_, _) => throw new InvalidOperationException("Unexpected connection.")));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadAsync(cancellation.Token));
    }

    [Fact]
    public async Task Reader_passes_cancellation_to_connection_factory_and_preserves_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("Connection unavailable.");
        var receivedToken = CancellationToken.None;
        var services = new ServiceCollection();
        services.UseSqlServerAdoNetOutbox(options => options.UseWorkerConnectionFactory((_, token) =>
        {
            receivedToken = token;
            throw failure;
        }));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await reader.ReadAsync(cancellation.Token));

        Assert.Same(failure, exception);
        Assert.Equal(cancellation.Token, receivedToken);
    }
}
