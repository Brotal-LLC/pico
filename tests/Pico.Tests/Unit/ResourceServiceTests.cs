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
    private static readonly Guid FlavorId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();

    [Fact]
    public async Task ProvisionAsync_BackendSucceeds_StoresResourceInProvisioningStatus()
    {
        var repo = new FakeResourceRepository();
        var backend = new FakeProvisioningBackend();
        var service = new ResourceService(repo, backend);

        var result = await service.ProvisionAsync(UserId,
            new ProvisionRequestDto("my-vm", FlavorId, ImageId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.Resources);

        var resource = repo.Resources.Values.Single();
        Assert.Equal("my-vm", resource.Name);
        Assert.Equal("fake-" + resource.Id.ToString("N"), resource.ExternalId);
        Assert.Equal("10.0.0.42", resource.IpAddress);

        // After ProvisionAsync, status should be Provisioning (state walked through to it)
        Assert.Equal(Domain.Enums.ResourceStatus.Provisioning, resource.Status);
        Assert.NotEmpty(repo.Events[resource.Id]);
    }

    [Fact]
    public async Task ProvisionAsync_BackendFails_FailsResultAndNoStatusChange()
    {
        var repo = new FakeResourceRepository();
        var backend = new FakeProvisioningBackend { ProvisionShouldFail = true };
        var service = new ResourceService(repo, backend);

        var result = await service.ProvisionAsync(UserId,
            new ProvisionRequestDto("x", FlavorId, ImageId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Backend declined", result.ErrorMessage);
        Assert.Single(repo.Resources);
        // Created state since backend failed
        Assert.Equal(Domain.Enums.ResourceStatus.Created, repo.Resources.Values.Single().Status);
    }

    [Fact]
    public async Task StartAsync_StoppedResource_TransitionsToRunning()
    {
        var repo = new FakeResourceRepository();
        var backend = new FakeProvisioningBackend();
        var service = new ResourceService(repo, backend);

        // Provision first
        var p = await service.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", FlavorId, ImageId), CancellationToken.None);
        var resourceId = p.Value!.Id;

        // Manually walk the resource to Stopped for the test (simulates: provisioned, started, then stopped)
        var resource = repo.Resources[resourceId];
        resource.TransitionTo(Domain.Enums.ResourceStatus.Running, "manual");  // Provisioning → Running
        resource.TransitionTo(Domain.Enums.ResourceStatus.Stopped, "manual");  // Running → Stopped

        // Now StartAsync should take Stopped → Running
        var result = await service.StartAsync(resourceId, UserId, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(Domain.Enums.ResourceStatus.Running, repo.Resources[resourceId].Status);
    }

    [Fact]
    public async Task StartAsync_OtherUsersResource_ReturnsForbidden()
    {
        var repo = new FakeResourceRepository();
        var backend = new FakeProvisioningBackend();
        var service = new ResourceService(repo, backend);

        var p = await service.ProvisionAsync(UserId,
            new ProvisionRequestDto("vm", FlavorId, ImageId), CancellationToken.None);
        var resourceId = p.Value!.Id;

        var differentUser = Guid.NewGuid();
        var result = await service.StartAsync(resourceId, differentUser, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("Forbidden", result.ErrorMessage);
    }

    [Fact]
    public async Task ListUserResourcesAsync_ReturnsOnlyOwn()
    {
        var repo = new FakeResourceRepository();
        var backend = new FakeProvisioningBackend();
        var service = new ResourceService(repo, backend);

        await service.ProvisionAsync(UserId,
            new ProvisionRequestDto("mine-1", FlavorId, ImageId), CancellationToken.None);
        await service.ProvisionAsync(UserId,
            new ProvisionRequestDto("mine-2", FlavorId, ImageId), CancellationToken.None);

        var other = Guid.NewGuid();
        await service.ProvisionAsync(other,
            new ProvisionRequestDto("other-1", FlavorId, ImageId), CancellationToken.None);

        var list = await service.ListUserResourcesAsync(UserId, CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.All(list, r => Assert.Equal(UserId, repo.Resources[r.Id].UserId));
    }
}
