using Pico.Domain.Enums;

namespace Pico.Domain.Entities;

/// <summary>
/// Append-only audit log of a single state change on a Resource.
/// SSE endpoint reads this for live updates.
/// </summary>
public class ResourceEvent
{
    public Guid Id { get; private set; }
    public Guid ResourceId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public ResourceStatus OldStatus { get; private set; }
    public ResourceStatus NewStatus { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset Timestamp { get; private set; }

    private ResourceEvent() { }

    public static ResourceEvent Create(
        Guid resourceId,
        string eventType,
        ResourceStatus oldStatus,
        ResourceStatus newStatus,
        string message)
    {
        return new ResourceEvent
        {
            Id = Guid.NewGuid(),
            ResourceId = resourceId,
            EventType = eventType ?? "StatusChange",
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Message = message ?? string.Empty,
            Timestamp = DateTimeOffset.UtcNow,
        };
    }
}
