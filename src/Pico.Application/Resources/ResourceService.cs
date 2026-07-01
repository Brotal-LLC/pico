using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Domain.StateMachines;

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
/// </summary>
public class ResourceService
{
    private readonly IResourceRepository _resources;
    private readonly IFlavorRepository _flavors;
    private readonly IImageRepository _images;
    private readonly IProvisioningBackend _backend;

    public ResourceService(
        IResourceRepository resources,
        IFlavorRepository flavors,
        IImageRepository images,
        IProvisioningBackend backend)
    {
        _resources = resources;
        _flavors = flavors;
        _images = images;
        _backend = backend;
    }

    public async Task<Result<ResourceSummaryDto>> ProvisionAsync(
        Guid userId, ProvisionRequestDto req, CancellationToken ct)
    {
        // Validate flavor and image exist and are active
        var flavor = await _flavors.FindByIdAsync(req.FlavorId, ct);
        if (flavor is null || !flavor.Active)
            return Result<ResourceSummaryDto>.Failure("Flavor not found or inactive");

        var image = await _images.FindByIdAsync(req.ImageId, ct);
        if (image is null)
            return Result<ResourceSummaryDto>.Failure("Image not found");

        // Create the resource entity in Created state
        var resource = Resource.Provision(userId, req.FlavorId, req.ImageId, req.Name);
        await _resources.AddAsync(resource, ct);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "Created", ResourceStatus.Created, ResourceStatus.Created, "Resource created"),
            ct);

        // Hand off to provisioning backend with resolved flavor/image details
        var provisionReq = new ProvisionRequest(
            resource.Id, resource.Name, req.FlavorId, req.ImageId, userId.ToString(),
            Vcpus: flavor.Vcpus, RamMb: flavor.RamMb, DiskGb: flavor.DiskGb,
            ImageName: image.Name);
        var result = await _backend.ProvisionAsync(provisionReq, ct);

        if (!result.Success)
        {
            // Transition Created → Provisioning → Failed to record the failure properly
            resource.TransitionTo(ResourceStatus.Provisioning, "Backend called");
            resource.TransitionTo(ResourceStatus.Failed, $"Backend error: {result.Error}");
            await _resources.UpdateAsync(resource, ct);
            await _resources.AddEventAsync(
                ResourceEvent.Create(resource.Id, "ProvisionFailed", ResourceStatus.Provisioning, ResourceStatus.Failed, result.Error ?? "Unknown error"),
                ct);
            return Result<ResourceSummaryDto>.Failure($"Provisioning backend failed: {result.Error}");
        }

        // Persist backend-assigned ids, then transition Created → Provisioning → Running
        resource.SetExternalId(result.ExternalId);
        resource.SetIpAddress(result.IpAddress);
        resource.TransitionTo(ResourceStatus.Provisioning, "Backend accepted");
        await _resources.UpdateAsync(resource, ct);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "StatusChange", ResourceStatus.Created, ResourceStatus.Provisioning, "Backend provisioning started"),
            ct);

        // For synchronous backends (mock/docker/fake), provisioning completes immediately
        if (_backend.Mode != "openstack")
        {
            resource.TransitionTo(ResourceStatus.Running, "Provisioning complete");
            await _resources.UpdateAsync(resource, ct);
            await _resources.AddEventAsync(
                ResourceEvent.Create(resource.Id, "StatusChange", ResourceStatus.Provisioning, ResourceStatus.Running, "Resource is now running"),
                ct);
        }

        return Result<ResourceSummaryDto>.Success(ToSummary(resource));
    }

    public async Task<Result<ResourceSummaryDto>> StartAsync(Guid resourceId, Guid userId, CancellationToken ct)
    {
        var resource = await _resources.FindByIdAsync(resourceId, ct);
        if (resource is null)
            return Result<ResourceSummaryDto>.Failure("Resource not found");
        if (resource.UserId != userId)
            return Result<ResourceSummaryDto>.Failure("Forbidden");

        var transitionFailure = ValidateTransition(resource, ResourceStatus.Running);
        if (transitionFailure is not null)
            return transitionFailure;

        if (resource.ExternalId is null)
            return Result<ResourceSummaryDto>.Failure("Resource has no external id");

        var backendResult = await _backend.StartAsync(resource.ExternalId, ct);
        if (!backendResult.Success)
            return Result<ResourceSummaryDto>.Failure($"Backend start failed: {backendResult.Error}");

        var prev = resource.Status;
        resource.TransitionTo(ResourceStatus.Running, "User started");
        await _resources.UpdateAsync(resource, ct);
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

        var transitionFailure = ValidateTransition(resource, ResourceStatus.Stopped);
        if (transitionFailure is not null)
            return transitionFailure;

        if (resource.ExternalId is null)
            return Result<ResourceSummaryDto>.Failure("Resource has no external id");

        var backendResult = await _backend.StopAsync(resource.ExternalId, ct);
        if (!backendResult.Success)
            return Result<ResourceSummaryDto>.Failure($"Backend stop failed: {backendResult.Error}");

        var prev = resource.Status;
        resource.TransitionTo(ResourceStatus.Stopped, "User stopped");
        await _resources.UpdateAsync(resource, ct);
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

        var transitionFailure = ValidateTransition(resource, ResourceStatus.Terminated);
        if (transitionFailure is not null)
            return transitionFailure;

        if (resource.ExternalId is not null)
        {
            var backendResult = await _backend.TerminateAsync(resource.ExternalId, ct);
            if (!backendResult.Success)
                return Result<ResourceSummaryDto>.Failure($"Backend terminate failed: {backendResult.Error}");
        }

        var prev = resource.Status;
        resource.TransitionTo(ResourceStatus.Terminated, "User terminated");
        await _resources.UpdateAsync(resource, ct);
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

    public async Task<ResourceDetailDto?> GetResourceDetailAsync(Guid resourceId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var resource = await _resources.FindByIdAsync(resourceId, ct);
        if (resource is null) return null;
        if (!isAdmin && resource.UserId != userId) return null;
        var events = await _resources.ListEventsAsync(resourceId, ct);
        return new ResourceDetailDto(
            resource.Id, resource.Name, resource.FlavorId, resource.ImageId,
            resource.Status.ToString(), resource.IpAddress, resource.ExternalId,
            resource.CreatedAt, resource.UpdatedAt,
            events.Select(e => new ResourceEventDto(
                e.Id, e.EventType, e.OldStatus.ToString(), e.NewStatus.ToString(),
                e.Message, e.Timestamp)).ToList());
    }

    public async Task<ResourceUsage?> GetUsageAsync(Guid resourceId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var resource = await _resources.FindByIdAsync(resourceId, ct);
        if (resource is null) return null;
        if (!isAdmin && resource.UserId != userId) return null;
        if (resource.ExternalId is null) return ResourceUsage.Empty();
        return await _backend.GetUsageAsync(resource.ExternalId, ct);
    }

    private static ResourceSummaryDto ToSummary(Resource r) =>
        new(r.Id, r.Name, r.Status.ToString(), r.FlavorId, r.ImageId,
            r.IpAddress, r.ExternalId, r.CreatedAt, r.UpdatedAt);

    private static Result<ResourceSummaryDto>? ValidateTransition(Resource resource, ResourceStatus targetStatus)
    {
        return ResourceStateMachine.CanTransition(resource.Status, targetStatus)
            ? null
            : Result<ResourceSummaryDto>.Failure($"Invalid transition: {resource.Status} -> {targetStatus}");
    }
}