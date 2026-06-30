using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain;
using Pico.Domain.Entities;
using Pico.Domain.Enums;

namespace Pico.Application.Resources;

public record ResourceSummaryDto(
    Guid Id,
    string Name,
    string Status,
    Guid FlavorId,
    Guid ImageId,
    string? IpAddress,
    string? ExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record ResourceDetailDto(
    Guid Id,
    string Name,
    Guid FlavorId,
    Guid ImageId,
    string Status,
    string? IpAddress,
    string? ExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ResourceEventDto> Events
);

public record ResourceEventDto(
    Guid Id,
    string EventType,
    string OldStatus,
    string NewStatus,
    string Message,
    DateTimeOffset Timestamp
);

public record ProvisionRequestDto(
    string Name,
    Guid FlavorId,
    Guid ImageId
);

/// <summary>
/// Resource lifecycle: provision, start, stop, terminate. Coordinates
/// IProvisioningBackend (the I/O) with the IResourceRepository (persistence).
/// Pure orchestration — no DB calls, no HTTP — fully unit-testable.
/// </summary>
public class ResourceService
{
    private readonly IResourceRepository _resources;
    private readonly IProvisioningBackend _backend;

    public ResourceService(IResourceRepository resources, IProvisioningBackend backend)
    {
        _resources = resources;
        _backend = backend;
    }

    public async Task<Result<ResourceSummaryDto>> ProvisionAsync(
        Guid userId, ProvisionRequestDto req, CancellationToken ct)
    {
        // Create the resource entity in Created state
        var resource = Resource.Provision(userId, req.FlavorId, req.ImageId, req.Name);
        await _resources.AddAsync(resource, ct);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "Created", ResourceStatus.Created, ResourceStatus.Created, "Resource created"),
            ct);

        // Hand off to provisioning backend (the actual I/O)
        var provisionReq = new ProvisionRequest(resource.Id, resource.Name, req.FlavorId, req.ImageId, userId.ToString());
        var result = await _backend.ProvisionAsync(provisionReq, ct);

        if (!result.Success)
        {
            return Result<ResourceSummaryDto>.Failure(
                $"Provisioning backend failed: {result.Error}");
        }

        // Persist backend-assigned ids, then transition Created → Provisioning
        resource.SetExternalId(result.ExternalId);
        resource.SetIpAddress(result.IpAddress);
        resource.TransitionTo(ResourceStatus.Provisioning, "Backend accepted");
        _resources.Update(resource);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "StatusChange", ResourceStatus.Created, ResourceStatus.Provisioning, "Backend provisioning started"),
            ct);

        return Result<ResourceSummaryDto>.Success(ToSummary(resource));
    }

    public async Task<Result<ResourceSummaryDto>> StartAsync(Guid resourceId, Guid userId, CancellationToken ct)
    {
        var resource = await _resources.FindByIdAsync(resourceId, ct);
        if (resource is null)
            return Result<ResourceSummaryDto>.Failure("Resource not found");
        if (resource.UserId != userId)
            return Result<ResourceSummaryDto>.Failure("Forbidden");

        var prev = resource.Status;
        resource.TransitionTo(ResourceStatus.Running, "User started");
        var backend = await _backend.StartAsync(resource.ExternalId ?? throw new InvalidOperationException("No external id"), ct);
        if (!backend.Success)
            return Result<ResourceSummaryDto>.Failure($"Backend start failed: {backend.Error}");

        _resources.Update(resource);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "StatusChange", prev, ResourceStatus.Running, "User started resource"),
            ct);
        return Result<ResourceSummaryDto>.Success(ToSummary(resource));
    }

    public async Task<Result<ResourceSummaryDto>> StopAsync(Guid resourceId, Guid userId, CancellationToken ct)
    {
        var resource = await _resources.FindByIdAsync(resourceId, ct);
        if (resource is null)
            return Result<ResourceSummaryDto>.Failure("Resource not found");
        if (resource.UserId != userId)
            return Result<ResourceSummaryDto>.Failure("Forbidden");

        var prev = resource.Status;
        resource.TransitionTo(ResourceStatus.Stopped, "User stopped");
        var backend = await _backend.StopAsync(resource.ExternalId ?? throw new InvalidOperationException("No external id"), ct);
        if (!backend.Success)
            return Result<ResourceSummaryDto>.Failure($"Backend stop failed: {backend.Error}");

        _resources.Update(resource);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "StatusChange", prev, ResourceStatus.Stopped, "User stopped resource"),
            ct);
        return Result<ResourceSummaryDto>.Success(ToSummary(resource));
    }

    public async Task<Result<ResourceSummaryDto>> TerminateAsync(Guid resourceId, Guid userId, CancellationToken ct)
    {
        var resource = await _resources.FindByIdAsync(resourceId, ct);
        if (resource is null)
            return Result<ResourceSummaryDto>.Failure("Resource not found");
        if (resource.UserId != userId)
            return Result<ResourceSummaryDto>.Failure("Forbidden");

        var prev = resource.Status;
        resource.TransitionTo(ResourceStatus.Terminated, "User terminated");
        var backend = await _backend.TerminateAsync(resource.ExternalId ?? throw new InvalidOperationException("No external id"), ct);
        if (!backend.Success)
            return Result<ResourceSummaryDto>.Failure($"Backend terminate failed: {backend.Error}");

        _resources.Update(resource);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "StatusChange", prev, ResourceStatus.Terminated, "User terminated resource"),
            ct);
        return Result<ResourceSummaryDto>.Success(ToSummary(resource));
    }

    public async Task<IReadOnlyList<ResourceSummaryDto>> ListUserResourcesAsync(Guid userId, CancellationToken ct)
    {
        var resources = await _resources.ListByUserAsync(userId, ct);
        return resources.Select(ToSummary).ToList();
    }

    public async Task<ResourceDetailDto?> GetResourceDetailAsync(Guid resourceId, CancellationToken ct)
    {
        var resource = await _resources.FindByIdAsync(resourceId, ct);
        if (resource is null) return null;
        var events = await _resources.ListEventsAsync(resourceId, ct);
        return new ResourceDetailDto(
            resource.Id, resource.Name, resource.FlavorId, resource.ImageId,
            resource.Status.ToString(), resource.IpAddress, resource.ExternalId,
            resource.CreatedAt, resource.UpdatedAt,
            events.Select(e => new ResourceEventDto(
                e.Id, e.EventType, e.OldStatus.ToString(), e.NewStatus.ToString(),
                e.Message, e.Timestamp)).ToList());
    }

    public Task<ResourceUsage> GetUsageAsync(Guid resourceId, CancellationToken ct)
    {
        // ResourceService is pure orchestration; usage lookups delegate to backend.
        // We pull the resource first to get externalId.
        return _backend.GetUsageAsync(resourceId.ToString(), ct);
    }

    private static ResourceSummaryDto ToSummary(Resource r) =>
        new(r.Id, r.Name, r.Status.ToString(), r.FlavorId, r.ImageId,
            r.IpAddress, r.ExternalId, r.CreatedAt, r.UpdatedAt);
}
