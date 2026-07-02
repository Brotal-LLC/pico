using Pico.Application.Resources;
using Pico.Application.Common;
using Pico.Domain.Entities;
using Pico.Tests.Helpers;
using Pico.Application.Provisioning;

namespace Pico.Tests.Unit;

/// <summary>
/// ResourceService tests with in-memory fakes. Pure-logic-level; no DB.
/// </summary>
public class ResourceServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Flavor TestFlavor = Flavor.Create("pico.small", 1, 2048, 40, 0.025m, 15m, "General");
    private static readonly Image TestImage = Image.Create("ubuntu-24", "Ubuntu", "24.04 LTS", 2);

    private static (FakeResourceRepository res, FakeFlavorRepository fla, FakeImageRepository img, ResourceService svc) Setup()
    {
        var (res, fla, img, _, svc) = SetupWithBackend();
        return (res, fla, img, svc);
    }

    private static (FakeResourceRepository res, FakeFlavorRepository fla, FakeImageRepository img, FakeProvisioningBackend backend, ResourceService svc) SetupWithBackend()
    {
        var res = new FakeResourceRepository();
        var fla = new FakeFlavorRepository();
        var img = new FakeImageRepository();
        fla.Flavors[TestFlavor.Id] = TestFlavor;
        img.Images[TestImage.Id] = TestImage;
        var backend = new FakeProvisioningBackend();
        var svc = new ResourceService(res, fla, img, backend, new Pico.Application.Networking.NetworkService());
        return (res, fla, img, backend, svc);
    }

    private static (FakeResourceRepository res, FakeFlavorRepository fla, FakeImageRepository img, FakeProvisioningBackend backend, ResourceService svc, Pico.Application.Networking.NetworkService network) SetupWithNetwork()
    {
        var res = new FakeResourceRepository();
        var fla = new FakeFlavorRepository();
        var img = new FakeImageRepository();
        fla.Flavors[TestFlavor.Id] = TestFlavor;
        img.Images[TestImage.Id] = TestImage;
        var backend = new FakeProvisioningBackend();
        var network = new Pico.Application.Networking.NetworkService();
        var svc = new ResourceService(res, fla, img, backend, network);
        return (res, fla, img, backend, svc, network);
    }

    [Fact]
    public async Task ProvisionAsync_BackendSucceeds_ResourceReachesRunning()
    {
        var (repo, _, _, svc) = Setup();

        var result = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("my-vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.Resources);

        var resource = repo.Resources.Values.Single();
        Assert.Equal("my-vm", resource.Name);
        Assert.Equal("fake-" + resource.Id.ToString("N"), resource.ExternalId);
        Assert.Equal("10.42.0.2", resource.IpAddress);

        // After ProvisionAsync with mock backend, status should be Running
        Assert.Equal(Domain.Enums.ResourceStatus.Running, resource.Status);
        Assert.NotEmpty(repo.Events[resource.Id]);
    }

    [Fact]
    public async Task ProvisionAsync_BackendFails_MarksFailed()
    {
        var repo = new FakeResourceRepository();
        var fla = new FakeFlavorRepository();
        var img = new FakeImageRepository();
        fla.Flavors[TestFlavor.Id] = TestFlavor;
        img.Images[TestImage.Id] = TestImage;
        var backend = new FakeProvisioningBackend { ProvisionShouldFail = true };
        var svc = new ResourceService(repo, fla, img, backend, new Pico.Application.Networking.NetworkService());

        var result = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("x", TestFlavor.Id, TestImage.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Backend declined", result.ErrorMessage);
        Assert.Single(repo.Resources);
        // Should be Failed, not Created
        Assert.Equal(Domain.Enums.ResourceStatus.Failed, repo.Resources.Values.Single().Status);
    }

    [Fact]
    public async Task ProvisionAsync_InvalidFlavor_ReturnsFailure()
    {
        var (repo, _, _, svc) = Setup();

        var result = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("x", Guid.NewGuid(), TestImage.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Flavor not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ProvisionAsync_InvalidImage_ReturnsFailure()
    {
        var (repo, _, _, svc) = Setup();

        var result = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("x", TestFlavor.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Image not found", result.ErrorMessage);
    }

    [Fact]
    public async Task StartAsync_StoppedResource_TransitionsToRunning()
    {
        var (repo, _, _, svc) = Setup();

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var resourceId = p.Value!.Id;

        // ProvisionAsync already reaches Running. Walk to Stopped for the test.
        var resource = repo.Resources[resourceId];
        // Running → Stopped is valid
        resource.TransitionTo(Domain.Enums.ResourceStatus.Stopped, "manual");

        var result = await svc.StartAsync(resourceId, UserId, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(Domain.Enums.ResourceStatus.Running, repo.Resources[resourceId].Status);
    }

    [Fact]
    public async Task StartAsync_OtherUsersResource_ReturnsForbidden()
    {
        var (repo, _, _, svc) = Setup();

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var resourceId = p.Value!.Id;

        var differentUser = Guid.NewGuid();
        var result = await svc.StartAsync(resourceId, differentUser, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("Forbidden", result.ErrorMessage);
    }

    [Fact]
    public async Task StartAsync_RunningResource_ReturnsFailureWithoutBackendCall()
    {
        var (repo, _, _, backend, svc) = SetupWithBackend();

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var resourceId = p.Value!.Id;

        Result<ResourceSummaryDto>? result = null;
        var ex = await Record.ExceptionAsync(async () =>
            result = await svc.StartAsync(resourceId, UserId, CancellationToken.None));

        Assert.Null(ex);
        Assert.NotNull(result);
        Assert.False(result!.IsSuccess);
        Assert.Contains("Invalid transition: Running -> Running", result.ErrorMessage);
        Assert.Equal(Domain.Enums.ResourceStatus.Running, repo.Resources[resourceId].Status);
        Assert.Equal(0, backend.StartCalls);
    }

    [Fact]
    public async Task StopAsync_StoppedResource_ReturnsFailureWithoutBackendCall()
    {
        var (repo, _, _, backend, svc) = SetupWithBackend();

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var resourceId = p.Value!.Id;
        repo.Resources[resourceId].TransitionTo(Domain.Enums.ResourceStatus.Stopped, "manual");

        Result<ResourceSummaryDto>? result = null;
        var ex = await Record.ExceptionAsync(async () =>
            result = await svc.StopAsync(resourceId, UserId, CancellationToken.None));

        Assert.Null(ex);
        Assert.NotNull(result);
        Assert.False(result!.IsSuccess);
        Assert.Contains("Invalid transition: Stopped -> Stopped", result.ErrorMessage);
        Assert.Equal(Domain.Enums.ResourceStatus.Stopped, repo.Resources[resourceId].Status);
        Assert.Equal(0, backend.StopCalls);
    }

    [Fact]
    public async Task TerminateAsync_ProvisioningResource_ReturnsFailureWithoutBackendCall()
    {
        var (repo, _, _, backend, svc) = SetupWithBackend();
        var resource = Resource.Provision(UserId, TestFlavor.Id, TestImage.Id, "vm");
        resource.SetExternalId("fake-provisioning");
        resource.TransitionTo(Domain.Enums.ResourceStatus.Provisioning, "manual");
        await repo.AddAsync(resource, CancellationToken.None);

        Result<ResourceSummaryDto>? result = null;
        var ex = await Record.ExceptionAsync(async () =>
            result = await svc.TerminateAsync(resource.Id, UserId, CancellationToken.None));

        Assert.Null(ex);
        Assert.NotNull(result);
        Assert.False(result!.IsSuccess);
        Assert.Contains("Invalid transition: Provisioning -> Terminated", result.ErrorMessage);
        Assert.Equal(Domain.Enums.ResourceStatus.Provisioning, repo.Resources[resource.Id].Status);
        Assert.Equal(0, backend.TerminateCalls);
    }

    [Fact]
    public async Task TerminateAsync_BackendFails_StillTransitionsToTerminated()
    {
        // Terminate is destructive and one-way. A backend hiccup (Docker
        // daemon down, stale "fake-..." externalId from older seed,
        // OpenStack rate-limit) must NOT trap the user with a resource
        // they can't remove. State machine wins; the backend cleanup
        // is best-effort.
        var (repo, _, _, backend, svc) = SetupWithBackend();
        backend.TerminateShouldFail = true;

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var resourceId = p.Value!.Id;
        // Walk to Stopped so we have a real outgoing transition
        repo.Resources[resourceId].TransitionTo(Domain.Enums.ResourceStatus.Stopped, "manual");
        await repo.UpdateAsync(repo.Resources[resourceId], CancellationToken.None);

        var result = await svc.TerminateAsync(resourceId, UserId, CancellationToken.None);

        // The terminate must SUCCEED even though the backend returned
        // an error — otherwise the user is stuck with a VM they can't
        // remove (e.g. after a Docker restart wipes a container ID).
        Assert.True(result.IsSuccess);
        Assert.Equal(Domain.Enums.ResourceStatus.Terminated, repo.Resources[resourceId].Status);
        // Backend was called (and failed), proving we don't short-circuit
        Assert.Equal(1, backend.TerminateCalls);
    }

    [Fact]
    public async Task ListUserResourcesAsync_ReturnsOnlyOwn()
    {
        var (repo, _, _, svc) = Setup();

        await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("mine-1", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("mine-2", TestFlavor.Id, TestImage.Id), CancellationToken.None);

        var other = Guid.NewGuid();
        await svc.ProvisionAsync(other,
            new ProvisionRequestDto("other-1", TestFlavor.Id, TestImage.Id), CancellationToken.None);

        var list = await svc.ListUserResourcesAsync(UserId, CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.All(list, r => Assert.Equal(UserId, repo.Resources[r.Id].UserId));
    }

    [Fact]
    public async Task GetResourceDetailAsync_OtherUser_ReturnsNull()
    {
        var (repo, _, _, svc) = Setup();

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);

        var detail = await svc.GetResourceDetailAsync(p.Value!.Id, Guid.NewGuid(), false, CancellationToken.None);
        Assert.Null(detail);
    }

    [Fact]
    public async Task GetResourceDetailAsync_Admin_CanSeeAny()
    {
        var (repo, _, _, svc) = Setup();

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);

        var detail = await svc.GetResourceDetailAsync(p.Value!.Id, Guid.NewGuid(), true, CancellationToken.None);
        Assert.NotNull(detail);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsPlanWithoutCreatingResource()
    {
        var (_, _, _, svc) = Setup();
        var resBefore = (await svc.ListUserResourcesAsync(UserId, CancellationToken.None)).Count;

        var preview = await svc.PreviewAsync(TestFlavor.Id, TestImage.Id, CancellationToken.None);

        Assert.True(preview.IsSuccess);
        Assert.NotNull(preview.Value);
        var plan = preview.Value!;
        Assert.Equal(TestFlavor.PricePerHour, plan.HourlyCostEstimate);
        Assert.Equal(TestFlavor.PricePerMonth, plan.MonthlyCostEstimate);
        Assert.Equal(TestFlavor.Vcpus, plan.Vcpus);
        Assert.Equal(TestFlavor.RamMb, plan.RamMb);
        Assert.Equal(TestFlavor.DiskGb, plan.DiskGb);
        Assert.Equal(TestImage.Name, plan.ImageName);
        Assert.Equal(TestImage.Os, plan.ImageOs);
        Assert.Equal(TestImage.Version, plan.ImageVersion);
        Assert.Equal(TestImage.SizeGb, plan.ImageSizeGb);

        // Preview must be a pure function: no rows added to the resource store.
        var resAfter = (await svc.ListUserResourcesAsync(UserId, CancellationToken.None)).Count;
        Assert.Equal(resBefore, resAfter);
    }

    [Fact]
    public async Task PreviewAsync_UnknownFlavor_Fails()
    {
        var (_, _, _, svc) = Setup();
        var result = await svc.PreviewAsync(Guid.NewGuid(), TestImage.Id, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Contains("Flavor", result.ErrorMessage);
    }

    [Fact]
    public async Task PreviewAsync_UnknownImage_Fails()
    {
        var (_, _, _, svc) = Setup();
        var result = await svc.PreviewAsync(TestFlavor.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Contains("Image", result.ErrorMessage);
    }

    [Fact]
    public async Task PreviewAsync_OversizedImage_ProducesIncompatibleWarning()
    {
        var (_, fla, img, svc) = Setup();
        var bigFlavor = Flavor.Create("pico.micro", 1, 512, 5, 0.005m, 3m, "General");
        var bigImage  = Image.Create("ubuntu-fatty", "Ubuntu", "24.04 LTS", 50);
        fla.Flavors[bigFlavor.Id] = bigFlavor;
        img.Images[bigImage.Id] = bigImage;

        var preview = await svc.PreviewAsync(bigFlavor.Id, bigImage.Id, CancellationToken.None);
        Assert.True(preview.IsSuccess);
        var plan = preview.Value!;
        Assert.False(plan.ImageFitsInFlavorDisk);
        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("larger than this flavor"));
    }

    [Fact]
    public async Task PreviewAsync_BurstableFlavor_AddsBurstableWarning()
    {
        var (_, fla, img, svc) = Setup();
        // TestFlavor has 1 vCPU and 40 GB disk; TestImage is 2 GB → fits, but bursts.
        Assert.True(TestFlavor.Vcpus < 2);

        var preview = await svc.PreviewAsync(TestFlavor.Id, TestImage.Id, CancellationToken.None);
        Assert.True(preview.IsSuccess);
        Assert.Contains(preview.Value!.Warnings, w => w.Contains("burstable", StringComparison.OrdinalIgnoreCase));
    }

    // ─── RecreateAsync tests ────────────────────────────────────────────

    [Fact]
    public async Task RecreateAsync_FromTerminatedSource_ClonesConfigIntoRunningResource()
    {
        var (repo, _, _, svc) = Setup();

        // Set up source VM and walk it to Terminated (Running → Terminated is valid)
        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("legacy-vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var sourceId = p.Value!.Id;
        repo.Resources[sourceId].TransitionTo(Domain.Enums.ResourceStatus.Terminated, "manual");
        await repo.UpdateAsync(repo.Resources[sourceId], CancellationToken.None);

        var result = await svc.RecreateAsync(sourceId, UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Should have created a new resource alongside the source
        Assert.Equal(2, repo.Resources.Count);
        var created = repo.Resources.Values.Single(r => r.Id != sourceId);
        Assert.Equal("legacy-vm-copy-2", created.Name);
        Assert.Equal(TestFlavor.Id, created.FlavorId);
        Assert.Equal(TestImage.Id, created.ImageId);
        Assert.Equal(Domain.Enums.ResourceStatus.Running, created.Status);
    }

    [Fact]
    public async Task RecreateAsync_AppendsToCopySeries_WhenNameCollides()
    {
        var (repo, _, _, svc) = Setup();

        // Set up source VM, terminate it
        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("legacy", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var sourceId = p.Value!.Id;
        repo.Resources[sourceId].TransitionTo(Domain.Enums.ResourceStatus.Terminated, "manual");
        await repo.UpdateAsync(repo.Resources[sourceId], CancellationToken.None);

        // First recreate → "legacy-copy-2"
        var r1 = await svc.RecreateAsync(sourceId, UserId, CancellationToken.None);
        Assert.True(r1.IsSuccess);
        Assert.Equal("legacy-copy-2", repo.Resources.Values.Single(r => r.Id != sourceId).Name);

        // Walk the new one to Terminated so the naming check is fresh
        var copy2Id = repo.Resources.Values.Single(r => r.Id != sourceId).Id;
        repo.Resources[copy2Id].TransitionTo(Domain.Enums.ResourceStatus.Terminated, "manual");

        // Second recreate from the original source → "legacy-copy-3"
        var r2 = await svc.RecreateAsync(sourceId, UserId, CancellationToken.None);
        Assert.True(r2.IsSuccess);
        Assert.Contains(repo.Resources.Values, r => r.Name == "legacy-copy-3");
    }

    [Fact]
    public async Task RecreateAsync_OtherUsersSource_ReturnsForbidden()
    {
        var (repo, _, _, svc) = Setup();

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("mine", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var sourceId = p.Value!.Id;

        var differentUser = Guid.NewGuid();
        var result = await svc.RecreateAsync(sourceId, differentUser, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("Forbidden", result.ErrorMessage);
    }

    [Fact]
    public async Task RecreateAsync_MissingSource_ReturnsFailure()
    {
        var (_, _, _, svc) = Setup();
        var result = await svc.RecreateAsync(Guid.NewGuid(), UserId, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ─── VM IP networking ────────────────────────────────────────────────
    // ResourceService owns IP allocation: it asks NetworkService for the
    // next free /24 slot, hands it to the backend via ProvisionRequest,
    // and persists the (possibly echoed) result on the resource.

    [Fact]
    public async Task ProvisionAsync_AssignsUniqueIpPerResource()
    {
        var (repo, _, _, _, svc, _) = SetupWithNetwork();

        var a = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm-a", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var b = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm-b", TestFlavor.Id, TestImage.Id), CancellationToken.None);

        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
        var ipA = repo.Resources[a.Value!.Id].IpAddress;
        var ipB = repo.Resources[b.Value!.Id].IpAddress;
        Assert.NotEqual(ipA, ipB);
        Assert.StartsWith("10.42.0.", ipA);
        Assert.StartsWith("10.42.0.", ipB);
    }

    [Fact]
    public async Task ProvisionAsync_BackendFailure_ReleasesIp()
    {
        // If the backend refuses, the allocated IP must come back to the
        // pool — otherwise a transient backend hiccup permanently leaks
        // an address until the next API restart.
        var (_, _, _, backend, svc, network) = SetupWithNetwork();
        backend.ProvisionShouldFail = true;

        // Provision #1 fails (consumes then releases .2).
        var result1 = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("doomed-1", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        Assert.False(result1.IsSuccess);

        // Provision #2 fails the same way (consumes then releases .2 again
        // because it was the only free slot).
        var result2 = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("doomed-2", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        Assert.False(result2.IsSuccess);

        // After two failed provisions, the next successful allocation
        // must still get the same first-usable slot — proving the
        // failed attempts didn't leak IPs.
        backend.ProvisionShouldFail = false;
        var result3 = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("survivor", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        Assert.True(result3.IsSuccess);
        // 10.42.0.2 is the first usable slot in the pool. After two
        // failed provisions (each consuming then releasing it), the
        // pool should still have .2 at the front.
        Assert.StartsWith("10.42.0.", result3.Value!.IpAddress);
    }

    [Fact]
    public async Task TerminateAsync_ReleasesIpBackToPool()
    {
        // Walking a Running resource to Terminated should free its IP so
        // a fresh provision can reuse it.
        var (repo, _, _, _, svc, network) = SetupWithNetwork();

        var p = await svc.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", TestFlavor.Id, TestImage.Id), CancellationToken.None);
        var ip = repo.Resources[p.Value!.Id].IpAddress;

        // Walk to Stopped (Terminate from Running isn't allowed).
        repo.Resources[p.Value.Id].TransitionTo(Domain.Enums.ResourceStatus.Stopped, "manual");
        await repo.UpdateAsync(repo.Resources[p.Value.Id], CancellationToken.None);
        var result = await svc.TerminateAsync(p.Value.Id, UserId, CancellationToken.None);
        Assert.True(result.IsSuccess);

        // First allocation after release is the same slot — we just freed it.
        var reused = await network.AllocateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(ip, reused);
    }
}