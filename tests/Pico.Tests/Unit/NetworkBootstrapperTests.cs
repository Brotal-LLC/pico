using Pico.Application.Common;
using Pico.Application.Networking;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Infrastructure.Networking;
using Pico.Tests.Helpers;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// NetworkBootstrapper is an IHostedService that hydrates the in-memory
/// IP allocator from existing non-Terminated resources at API startup.
/// On a cold start with N live resources holding IPs, the first
/// AllocateAsync after bootstrap must skip those slots — otherwise the
/// API would happily assign the same IP to two containers.
/// </summary>
public class NetworkBootstrapperTests
{
    [Fact]
    public async Task RunAsync_RepopulatesAllocatorFromRepository()
    {
        var repo = new FakeResourceRepository();
        var userId = Guid.NewGuid();
        var alive = Resource.Provision(userId, Guid.NewGuid(), Guid.NewGuid(), "alive-vm");
        alive.SetIpAddress("10.42.0.7");
        alive.TransitionTo(ResourceStatus.Provisioning, "test");
        alive.TransitionTo(ResourceStatus.Running, "test");
        repo.Resources[alive.Id] = alive;

        var network = new NetworkService();
        var boot = new NetworkBootstrapper(network, repo, new ListLogger<NetworkBootstrapper>());

        await boot.RunAsync(CancellationToken.None);

        // The live slot must NOT be reallocated.
        var next = await network.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.NotEqual("10.42.0.7", next);
    }

    [Fact]
    public async Task RunAsync_EmptyRepository_FirstAllocationReturnsFirstUsable()
    {
        var repo = new FakeResourceRepository();
        var network = new NetworkService();
        var boot = new NetworkBootstrapper(network, repo, new ListLogger<NetworkBootstrapper>());

        await boot.RunAsync(CancellationToken.None);

        var next = await network.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("10.42.0.2", next); // .1 is gateway, .0 is network address
    }

    [Fact]
    public async Task RunAsync_IsIdempotent()
    {
        var repo = new FakeResourceRepository();
        var network = new NetworkService();
        var boot = new NetworkBootstrapper(network, repo, new ListLogger<NetworkBootstrapper>());

        await boot.RunAsync(CancellationToken.None);
        // Second call should not throw or duplicate state.
        await boot.RunAsync(CancellationToken.None);

        var next = await network.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("10.42.0.2", next);
    }
}

/// <summary>Tiny ILogger that swallows output. Lets us construct IHostedService-style
/// classes in unit tests without dragging in Moq or a console sink.</summary>
internal sealed class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) { }
}