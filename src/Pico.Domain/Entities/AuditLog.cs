namespace Pico.Domain.Entities;

/// <summary>
/// Immutable record of any user-initiated action for security/audit purposes.
/// </summary>
public class AuditLog
{
    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid? EntityId { get; private set; }
    public string DetailsJson { get; private set; } = "{}";
    public DateTimeOffset Timestamp { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid? userId,
        string action,
        string entityType,
        Guid? entityId,
        string detailsJson = "{}")
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action ?? "Unknown",
            EntityType = entityType ?? "Unknown",
            EntityId = entityId,
            DetailsJson = detailsJson ?? "{}",
            Timestamp = DateTimeOffset.UtcNow,
        };
    }
}