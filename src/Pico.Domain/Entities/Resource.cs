using Pico.Domain.Enums;
using Pico.Domain.StateMachines;

namespace Pico.Domain.Entities;

/// <summary>
/// A provisioned VM/instance. Lifecycle enforced by ResourceStateMachine.
/// externalId = backend-specific id (Docker container id, Nova VM UUID, mock uuid)
/// ipAddress  = assigned IP, populated after provisioning completes.
/// </summary>
public class Resource
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FlavorId { get; private set; }
    public Guid ImageId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ResourceStatus Status { get; private set; }
    public string? ExternalId { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Resource() { }

    public static Resource Provision(Guid userId, Guid flavorId, Guid imageId, string name)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));
        if (flavorId == Guid.Empty)
            throw new ArgumentException("Flavor id is required.", nameof(flavorId));
        if (imageId == Guid.Empty)
            throw new ArgumentException("Image id is required.", nameof(imageId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        var now = DateTimeOffset.UtcNow;
        return new Resource
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FlavorId = flavorId,
            ImageId = imageId,
            Name = name.Trim(),
            Status = ResourceStatus.Created,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void TransitionTo(ResourceStatus newStatus, string message)
    {
        ResourceStateMachine.EnsureTransition(Status, newStatus);
        Status = newStatus;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetExternalId(string? externalId) => ExternalId = externalId?.Trim();
    public void SetIpAddress(string? ipAddress) => IpAddress = ipAddress?.Trim();

    public bool IsTerminated() => Status == ResourceStatus.Terminated;
    public bool IsRunning() => Status == ResourceStatus.Running;
    public bool IsStopped() => Status == ResourceStatus.Stopped;
    public bool IsFailed() => Status == ResourceStatus.Failed;
}
