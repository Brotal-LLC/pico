using Pico.Application.Networking;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Tests.Helpers;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// NetworkService allocates IPs from a fixed subnet (10.42.0.0/24) for
/// VM containers. .1 is reserved for the Docker gateway, .255 for
/// broadcast. The service tracks in-flight allocations in an
/// in-memory map so concurrent AllocateAsync calls never return the
/// same IP twice.
///
/// On startup the service is repopulated from existing non-Terminated
/// resources in the repository so containers recreated after an API
/// restart keep their IP.
/// </summary>
public class NetworkServiceTests
{
    [Fact]
    public async Task AllocateAsync_FirstCall_ReturnsFirstUsableIp()
    {
        var svc = new NetworkService();

        var ip = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("10.42.0.2", ip); // .1 is gateway
    }

    [Fact]
    public async Task AllocateAsync_TwoCalls_ReturnDistinctIps()
    {
        var svc = new NetworkService();

        var a = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        var b = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task AllocateAsync_ReleaseThenAllocate_ReusesFreedIp()
    {
        var svc = new NetworkService();

        var first = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        var second = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        await svc.ReleaseAsync(first, CancellationToken.None);
        var third = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(first, third); // the freed slot gets reused
    }

    [Fact]
    public async Task AllocateAsync_PoolExhausted_Throws()
    {
        var svc = new NetworkService();
        // /24 = 256 addresses. We reserve .0 (network), .1 (gateway), .255 (broadcast) → 253 usable slots (2..254).
        var allocated = new HashSet<string>();
        for (var i = 0; i < 253; i++)
            allocated.Add(await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None));

        // Sanity-check we filled the pool
        Assert.Contains("10.42.0.2", allocated);
        Assert.Contains("10.42.0.254", allocated);
        Assert.DoesNotContain("10.42.0.0", allocated);
        Assert.DoesNotContain("10.42.0.1", allocated);
        Assert.DoesNotContain("10.42.0.255", allocated);

        await Assert.ThrowsAsync<NetworkExhaustedException>(() =>
            svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RepopulateAsync_LoadsNonTerminatedResources()
    {
        var repo = new FakeResourceRepository();
        var userId = Guid.NewGuid();
        var alive = Resource.Provision(userId, Guid.NewGuid(), Guid.NewGuid(), "alive-vm");
        var dead = Resource.Provision(userId, Guid.NewGuid(), Guid.NewGuid(), "dead-vm");
        alive.SetIpAddress("10.42.0.42");
        alive.TransitionTo(ResourceStatus.Provisioning, "test");
        alive.TransitionTo(ResourceStatus.Running, "test");
        dead.SetIpAddress("10.42.0.43");
        dead.TransitionTo(ResourceStatus.Provisioning, "test");
        dead.TransitionTo(ResourceStatus.Running, "test");
        dead.TransitionTo(ResourceStatus.Terminated, "test");
        repo.Resources[alive.Id] = alive;
        repo.Resources[dead.Id] = dead;

        var svc = new NetworkService();
        await svc.RepopulateAsync(repo, CancellationToken.None);

        // The live VM's IP must be marked taken so the next Allocate doesn't hand it out
        var next = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.NotEqual("10.42.0.42", next);
        // The terminated VM's IP must be free again
        Assert.Equal("10.42.0.43", next);
    }

    [Fact]
    public async Task RepopulateAsync_IgnoresResourcesWithoutIp()
    {
        var repo = new FakeResourceRepository();
        var userId = Guid.NewGuid();
        var r = Resource.Provision(userId, Guid.NewGuid(), Guid.NewGuid(), "no-ip-vm");
        r.TransitionTo(ResourceStatus.Provisioning, "test");
        r.TransitionTo(ResourceStatus.Running, "test");
        repo.Resources[r.Id] = r;

        var svc = new NetworkService();
        await svc.RepopulateAsync(repo, CancellationToken.None);

        // Without an IP we can't reclaim a slot — pool is fresh, returns .2
        var next = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("10.42.0.2", next);
    }
}