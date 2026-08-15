using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinyEvents.Worker;
using Xunit;

namespace TinyEvents.Tests.Migrations;

public sealed class TinyEventsMigrationServiceProviderExtensionsTests
{
    private const string MissingProviderMessage =
        "No TinyEvents database provider is registered. Register exactly one " +
        "TinyEvents database provider before calling MigrateTinyEventsAsync.";

    [Fact]
    public async Task Generic_host_migrates_in_an_owned_async_scope()
    {
        var state = new MigrationState();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(state);
        builder.Services.AddScoped<ScopeMarker>();
        builder.Services.AddScoped<ITinyEventsMigrator, RecordingMigrator>();
        using var host = builder.Build();

        await host.Services.MigrateTinyEventsAsync();

        Assert.Equal(1, state.MigrationCalls);
        Assert.True(state.ScopeDisposed);
    }

    [Fact]
    public async Task Worker_only_host_can_migrate_before_starting()
    {
        var state = new MigrationState();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyEventsWorker();
        builder.Services.AddSingleton(state);
        builder.Services.AddScoped<ScopeMarker>();
        builder.Services.AddScoped<ITinyEventsMigrator, RecordingMigrator>();
        using var host = builder.Build();

        await host.Services.MigrateTinyEventsAsync();

        Assert.Equal(1, state.MigrationCalls);
        Assert.True(state.ScopeDisposed);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_unchanged()
    {
        var state = new MigrationState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddScoped<ScopeMarker>();
        services.AddScoped<ITinyEventsMigrator, RecordingMigrator>();
        await using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();

        await provider.MigrateTinyEventsAsync(cancellation.Token);

        Assert.Equal(cancellation.Token, state.CancellationToken);
    }

    [Fact]
    public async Task Missing_provider_throws_stable_error_and_disposes_scope()
    {
        var services = new TrackingRootServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => services.MigrateTinyEventsAsync());

        Assert.Equal(MissingProviderMessage, exception.Message);
        Assert.True(services.ScopeDisposedAsynchronously);
    }

    [Fact]
    public async Task Null_service_provider_is_rejected()
    {
        IServiceProvider services = null!;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => services.MigrateTinyEventsAsync());

        Assert.Equal("services", exception.ParamName);
    }

    private sealed class RecordingMigrator : ITinyEventsMigrator
    {
        private readonly MigrationState state;
        private readonly ScopeMarker scopeMarker;

        public RecordingMigrator(
            MigrationState state,
            ScopeMarker scopeMarker)
        {
            this.state = state;
            this.scopeMarker = scopeMarker;
        }

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            GC.KeepAlive(scopeMarker);
            state.MigrationCalls++;
            state.CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class ScopeMarker : IAsyncDisposable
    {
        private readonly MigrationState state;

        public ScopeMarker(MigrationState state)
        {
            this.state = state;
        }

        public ValueTask DisposeAsync()
        {
            state.ScopeDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MigrationState
    {
        public int MigrationCalls { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public bool ScopeDisposed { get; set; }
    }

    private sealed class TrackingRootServiceProvider
        : IServiceProvider,
          IServiceScopeFactory
    {
        public bool ScopeDisposedAsynchronously { get; private set; }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IServiceScopeFactory) ? this : null;
        }

        public IServiceScope CreateScope()
        {
            return new TrackingScope(this);
        }

        private sealed class TrackingScope
            : IServiceScope,
              IAsyncDisposable,
              IServiceProvider
        {
            private readonly TrackingRootServiceProvider root;

            internal TrackingScope(TrackingRootServiceProvider root)
            {
                this.root = root;
            }

            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
            {
                return null;
            }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync()
            {
                root.ScopeDisposedAsynchronously = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
