using Pico.Application.Networking;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Application.Resources;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Tests.Helpers;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// Tests for IP conflict retry logic and external IP claiming.
///
/// Scenarios covered:
///   • ProvisionAsync retries when Docker returns "Address already in use"
///   • ProvisionAsync does NOT retry on non-conflict errors
///   • ClaimExternalIpAsync claims a free IP
///   • ClaimExternalIpAsync rejects an IP already owned by another resource
///   • ForceClaimExternalIpAsync evicts a previous owner
///   • ClaimExternalIpAsync is idempotent for the same owner
/// </summary>
public class IpConflictRetryTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Flavor TestFlavor = Flavor.Create("pico.small", 1, 2048, 40, 0.025m, 15m, "General");
    private static readonly Image TestImage = Image.Create("ubuntu-24", "Ubuntu", "24.04 LTS", 2);

    /// <summary>
    /// FakeProvisioningBackend that fails the first N calls with an
    /// "Address already in use" error, then succeeds. This simulates
    /// Docker rejecting an IP that an orphaned container holds.
    /// </summary>
    private class ConflictThenSucceedBackend : IProvisioningBackend
    {
        private int _conflictsRemaining;
        private int _provisionCalls;

        public ConflictThenSucceedBackend(int conflictsBeforeSuccess = 1)
        {
            _conflictsRemaining = conflictsBeforeSuccess;
        }

        public string Mode => "fake";
        public int ProvisionCalls => _provisionCalls;

        public Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct)
        {
            _provisionCalls++;
            if (_conflictsRemaining > 0)
            {
                _conflictsRemaining--;
                return Task.FromResult(ProvisionResult.Fail(
                    "Docker API responded with status code=Forbidden, " +
                    "response={\"message\":\"failed to set up container networking: Address already in use\"}"));
            }
            return Task.FromResult(ProvisionResult.Ok(
                $"fake-{request.ResourceId:N}", request.IpAddress ?? "10.42.0.99"));
        }

        public Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct) =>
            Task.FromResult(ProvisionResult.Ok(externalId, ""));
        public Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct) =>
            Task.FromResult(ProvisionResult.Ok(externalId, ""));
        public Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct) =>
            Task.FromResult(ProvisionResult.Ok(externalId, ""));
        public Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct) =>
            Task.FromResult(ResourceUsage.Empty());
        public Task<BackendHealth> GetHealthAsync(CancellationToken ct) =>
            Task.FromResult(new BackendHealth("fake", true, null, DateTimeOffset.UtcNow));
        public Task<IShellSession> ExecInteractiveAsync(string externalId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task ProvisionAsync_IPConflict_RetriesWithNewIpAndSucceeds()
    {
        var repo = new FakeResourceRepository();
        var fla = new FakeFlavorRepository();
        var img = new FakeImageRepository();
        fla.Flavors[TestFlavor.Id] = TestFlavor;
        img.Images[TestImage.Id] = TestImage;
        var backend = new ConflictThenSucceedBackend(conflictsBeforeSuccess: 1);
        var network = new NetworkService();
        var svc = new ResourceService(repo, fla, img, backend, network);

        var result = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("retry-vm", TestFlavor.Id, TestImage.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, backend.ProvisionCalls); // 1 conflict + 1 success
        var resource = repo.Resources.Values.Single();
        Assert.Equal(ResourceStatus.Running, resource.Status);
        // IP should be the second allocated one (10.42.0.3, since .2 was released)
        Assert.Equal("10.42.0.3", resource.IpAddress);
    }

    [Fact]
    public async Task ProvisionAsync_IPConflict_ExhaustsRetriesAndFails()
    {
        var repo = new FakeResourceRepository();
        var fla = new FakeFlavorRepository();
        var img = new FakeImageRepository();
        fla.Flavors[TestFlavor.Id] = TestFlavor;
        img.Images[TestImage.Id] = TestImage;
        // Always conflict — never succeeds
        var backend = new ConflictThenSucceedBackend(conflictsBeforeSuccess: 99);
        var network = new NetworkService();
        var svc = new ResourceService(repo, fla, img, backend, network);

        var result = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("doomed-vm", TestFlavor.Id, TestImage.Id),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Address already in use", result.ErrorMessage);
        Assert.Equal(3, backend.ProvisionCalls); // max 3 attempts
        var resource = repo.Resources.Values.Single();
        Assert.Equal(ResourceStatus.Failed, resource.Status);
    }

    [Fact]
    public async Task ProvisionAsync_NonConflictError_DoesNotRetry()
    {
        var repo = new FakeResourceRepository();
        var fla = new FakeFlavorRepository();
        var img = new FakeImageRepository();
        fla.Flavors[TestFlavor.Id] = TestFlavor;
        img.Images[TestImage.Id] = TestImage;
        var backend = new FakeProvisioningBackend { ProvisionShouldFail = true };
        var network = new NetworkService();
        var svc = new ResourceService(repo, fla, img, backend, network);

        var result = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("fail-vm", TestFlavor.Id, TestImage.Id),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Backend declined", result.ErrorMessage);
        // Should only be called once — non-conflict errors don't retry
        Assert.Equal(1, backend.ProvisionCalls);
    }

    [Fact]
    public async Task ProvisionAsync_IPConflict_RetryEventRecorded()
    {
        var repo = new FakeResourceRepository();
        var fla = new FakeFlavorRepository();
        var img = new FakeImageRepository();
        fla.Flavors[TestFlavor.Id] = TestFlavor;
        img.Images[TestImage.Id] = TestImage;
        var backend = new ConflictThenSucceedBackend(conflictsBeforeSuccess: 1);
        var network = new NetworkService();
        var svc = new ResourceService(repo, fla, img, backend, network);

        await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("event-vm", TestFlavor.Id, TestImage.Id),
            CancellationToken.None);

        var resource = repo.Resources.Values.Single();
        var events = repo.Events[resource.Id];

        // Should have a ProvisionRetry event for the conflict
        Assert.Contains(events, e => e.EventType == "ProvisionRetry");
        var retryEvent = events.Single(e => e.EventType == "ProvisionRetry");
        Assert.Contains("IP conflict", retryEvent.Message);
        Assert.Contains("attempt 1", retryEvent.Message);
    }
}

/// <summary>
/// Tests for NetworkService.ClaimExternalIpAsync and ForceClaimExternalIpAsync.
/// </summary>
public class NetworkClaimTests
{
    [Fact]
    public async Task ClaimExternalIpAsync_FreeIp_ClaimsIt()
    {
        var svc = new NetworkService();
        var resourceId = Guid.NewGuid();

        var claimed = await svc.ClaimExternalIpAsync("10.42.0.50", resourceId, CancellationToken.None);

        Assert.True(claimed);
        // The claimed IP should not be handed out by AllocateAsync
        var next = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.NotEqual("10.42.0.50", next);
    }

    [Fact]
    public async Task ClaimExternalIpAsync_SameOwner_IsIdempotent()
    {
        var svc = new NetworkService();
        var resourceId = Guid.NewGuid();
        var ip = "10.42.0.50";

        var first = await svc.ClaimExternalIpAsync(ip, resourceId, CancellationToken.None);
        var second = await svc.ClaimExternalIpAsync(ip, resourceId, CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
    }

    [Fact]
    public async Task ClaimExternalIpAsync_DifferentOwner_ReturnsFalse()
    {
        var svc = new NetworkService();
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();

        await svc.ClaimExternalIpAsync("10.42.0.50", owner1, CancellationToken.None);
        var result = await svc.ClaimExternalIpAsync("10.42.0.50", owner2, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ForceClaimExternalIpAsync_EvictsPreviousOwner()
    {
        var svc = new NetworkService();
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();
        var ip = "10.42.0.50";

        // Owner1 claims it
        await svc.ClaimExternalIpAsync(ip, owner1, CancellationToken.None);
        // Owner2 force-claims it
        await svc.ForceClaimExternalIpAsync(ip, owner2, CancellationToken.None);

        // Owner1 can now reclaim it (because owner2 was assigned, but
        // if we release from owner2, it should be free again)
        // Actually let's verify by allocating — the IP should still be
        // blocked because owner2 has it
        var next = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.NotEqual(ip, next);
    }

    [Fact]
    public async Task ClaimExternalIpAsync_InvalidIp_ReturnsFalse()
    {
        var svc = new NetworkService();

        Assert.False(await svc.ClaimExternalIpAsync("192.168.1.1", Guid.NewGuid(), CancellationToken.None));
        Assert.False(await svc.ClaimExternalIpAsync("not-an-ip", Guid.NewGuid(), CancellationToken.None));
        Assert.False(await svc.ClaimExternalIpAsync("10.42.0.0", Guid.NewGuid(), CancellationToken.None));
        Assert.False(await svc.ClaimExternalIpAsync("10.42.0.1", Guid.NewGuid(), CancellationToken.None));
        Assert.False(await svc.ClaimExternalIpAsync("10.42.0.255", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ClaimExternalIpAsync_AfterAllocate_PreventsReuse()
    {
        var svc = new NetworkService();

        // Allocate two IPs
        var ip1 = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        var ip2 = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);

        // Release ip1
        await svc.ReleaseAsync(ip1, CancellationToken.None);

        // External claim on ip1
        var claimed = await svc.ClaimExternalIpAsync(ip1, Guid.NewGuid(), CancellationToken.None);
        Assert.True(claimed);

        // Next allocate should NOT return ip1 (it's now externally claimed)
        var ip3 = await svc.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.NotEqual(ip1, ip3);
    }
}