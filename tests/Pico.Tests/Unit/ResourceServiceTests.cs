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
        var svc = new ResourceService(res, fla, img, backend);
        return (res, fla, img, backend, svc);
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
        Assert.Equal("10.0.0.42", resource.IpAddress);

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
        var svc = new ResourceService(repo, fla, img, backend);

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
}