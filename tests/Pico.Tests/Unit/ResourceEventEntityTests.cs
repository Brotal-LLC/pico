using Pico.Domain.Entities;
using Pico.Domain.Enums;

namespace Pico.Tests.Unit;

public class ResourceEventEntityTests
{
    [Fact]
    public void Create_StoresAllFields()
    {
        var resourceId = Guid.NewGuid();
        var evt = ResourceEvent.Create(
            resourceId: resourceId,
            eventType: "StatusChange",
            oldStatus: ResourceStatus.Provisioning,
            newStatus: ResourceStatus.Running,
            message: "VM started successfully");

        Assert.NotEqual(Guid.Empty, evt.Id);
        Assert.Equal(resourceId, evt.ResourceId);
        Assert.Equal("StatusChange", evt.EventType);
        Assert.Equal(ResourceStatus.Provisioning, evt.OldStatus);
        Assert.Equal(ResourceStatus.Running, evt.NewStatus);
        Assert.Equal("VM started successfully", evt.Message);
        Assert.True(evt.Timestamp <= DateTimeOffset.UtcNow);
    }
}
