using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Domain.StateMachines;

namespace Pico.Tests.Unit;

public class ResourceEntityTests
{
    private static readonly Guid DefaultUserId = Guid.NewGuid();
    private static readonly Guid DefaultFlavorId = Guid.NewGuid();
    private static readonly Guid DefaultImageId = Guid.NewGuid();

    [Fact]
    public void Provision_ReturnsResourceInCreatedStatus()
    {
        var resource = Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, "my-vm");
        Assert.NotEqual(Guid.Empty, resource.Id);
        Assert.Equal("my-vm", resource.Name);
        Assert.Equal(ResourceStatus.Created, resource.Status);
        Assert.True(resource.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Provision_WithBlankName_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, ""));

    [Fact]
    public void Provision_WithEmptyUserId_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Resource.Provision(Guid.Empty, DefaultFlavorId, DefaultImageId, "name"));

    [Theory]
    [InlineData(ResourceStatus.Created, ResourceStatus.Provisioning)]
    [InlineData(ResourceStatus.Provisioning, ResourceStatus.Running)]
    public void TransitionTo_Valid_ChangesStatus(ResourceStatus from, ResourceStatus to)
    {
        var resource = Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, "x");
        // Walk from Created to `from` via valid transitions
        WalkTo(resource, from);
        resource.TransitionTo(to, "test");
        Assert.Equal(to, resource.Status);
    }

    [Fact]
    public void TransitionTo_Invalid_Throws()
    {
        var resource = Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, "x");
        Assert.Throws<DomainException>(() =>
            resource.TransitionTo(ResourceStatus.Running, "skip provisioning"));
    }

    [Fact]
    public void TransitionTo_SetsUpdatedAt()
    {
        var resource = Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, "x");
        var before = resource.UpdatedAt;
        resource.TransitionTo(ResourceStatus.Provisioning, "start");
        Assert.True(resource.UpdatedAt >= before);
    }

    [Fact]
    public void SetExternalId_StoresBackendResourceId()
    {
        var resource = Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, "x");
        resource.SetExternalId("container-abc-123");
        Assert.Equal("container-abc-123", resource.ExternalId);
    }

    [Fact]
    public void SetIpAddress_StoresIp()
    {
        var resource = Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, "x");
        resource.SetIpAddress("10.0.0.5");
        Assert.Equal("10.0.0.5", resource.IpAddress);
    }

    [Fact]
    public void IsTerminated_ReturnsTrueWhenStatusIsTerminated()
    {
        var resource = Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, "x");
        WalkTo(resource, ResourceStatus.Running);
        resource.TransitionTo(ResourceStatus.Terminated, "done");
        Assert.True(resource.IsTerminated());
    }

    [Fact]
    public void IsRunning_ReturnsTrueWhenStatusIsRunning()
    {
        var resource = Resource.Provision(DefaultUserId, DefaultFlavorId, DefaultImageId, "x");
        WalkTo(resource, ResourceStatus.Running);
        Assert.True(resource.IsRunning());
        Assert.False(resource.IsStopped());
    }

    // Helper: walk a fresh resource from Created to the target state via the state machine.
    private static void WalkTo(Resource r, ResourceStatus target)
    {
        var path = target switch
        {
            ResourceStatus.Created => Array.Empty<ResourceStatus>(),
            ResourceStatus.Provisioning => new[] { ResourceStatus.Provisioning },
            ResourceStatus.Running => new[] { ResourceStatus.Provisioning, ResourceStatus.Running },
            ResourceStatus.Stopped => new[] { ResourceStatus.Provisioning, ResourceStatus.Running, ResourceStatus.Stopped },
            ResourceStatus.Failed => new[] { ResourceStatus.Provisioning, ResourceStatus.Failed },
            ResourceStatus.Terminated => new[] { ResourceStatus.Provisioning, ResourceStatus.Running, ResourceStatus.Terminated },
            _ => throw new NotImplementedException()
        };
        foreach (var s in path) r.TransitionTo(s, "walk");
    }
}