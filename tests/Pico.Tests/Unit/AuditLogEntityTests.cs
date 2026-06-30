using Pico.Domain.Entities;

namespace Pico.Tests.Unit;

public class AuditLogEntityTests
{
    [Fact]
    public void Create_StoresAllFields()
    {
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var log = AuditLog.Create(userId, "ResourceProvision", "Resource", entityId, "{\"flavor\":\"small\"}");

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal(userId, log.UserId);
        Assert.Equal("ResourceProvision", log.Action);
        Assert.Equal("Resource", log.EntityType);
        Assert.Equal(entityId, log.EntityId);
        Assert.Equal("{\"flavor\":\"small\"}", log.DetailsJson);
        Assert.True(log.Timestamp <= DateTimeOffset.UtcNow);
    }
}
