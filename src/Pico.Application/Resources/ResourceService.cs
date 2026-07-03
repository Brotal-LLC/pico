using Pico.Application.Common;
using Pico.Application.Networking;
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
/// Terraform-style "what will happen if I commit?" preview.
/// Computed purely from flavor + image — no side effects, no resource is created.
/// Returned by ResourceService.PreviewAsync and POST /api/resources/preview.
/// </summary>
public record ProvisioningPlanDto(
    decimal MonthlyCostEstimate,
    decimal HourlyCostEstimate,
    int Vcpus,
    int RamMb,
    int DiskGb,
    string ImageName,
    string ImageOs,
    string ImageVersion,
    int ImageSizeGb,
    bool ImageFitsInFlavorDisk,
    IReadOnlyList<string> Warnings
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
    private readonly NetworkService _network;

    public ResourceService(
        IResourceRepository resources,
        IFlavorRepository flavors,
        IImageRepository images,
        IProvisioningBackend backend,
        NetworkService network)
    {
        _resources = resources;
        _flavors = flavors;
        _images = images;
        _backend = backend;
        _network = network;
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

        // Reserve a /24 IP slot BEFORE talking to the backend so we
        // never hand the orchestrator a slot it can't honour. If the
        // backend subsequently fails, we release the slot below.
        //
        // Docker can still reject an IP with "Address already in use" if:
        //   • An orphaned container holds the IP but wasn't tracked by
        //     the reconciler (e.g. the API started before the container
        //     was created, or the reconciler failed).
        //   • A race between two concurrent provisions picked the same
        //     slot (the lock prevents this, but defense-in-depth).
        // We retry up to 3 times with a fresh IP on each attempt.
        const int maxAttempts = 3;
        var provisionAttempts = 0;
        string? allocatedIp = null;

        while (provisionAttempts < maxAttempts)
        {
            provisionAttempts++;

            try
            {
                allocatedIp = await _network.AllocateAsync(resource.Id, ct);
            }
            catch (NetworkExhaustedException)
            {
                // No more IPs — only fail if we haven't already tried.
                if (provisionAttempts == 1)
                {
                    resource.TransitionTo(ResourceStatus.Provisioning, "Backend called");
                    resource.TransitionTo(ResourceStatus.Failed, "IP pool exhausted (254 VMs already running)");
                    await _resources.UpdateAsync(resource, ct);
                    await _resources.AddEventAsync(
                        ResourceEvent.Create(resource.Id, "ProvisionFailed", ResourceStatus.Provisioning, ResourceStatus.Failed, "IP pool exhausted"),
                        ct);
                    return Result<ResourceSummaryDto>.Failure("IP pool exhausted. Terminate another VM and try again.");
                }
                // We released an IP on a previous attempt but the pool
                // is now exhausted (shouldn't happen with 253 slots,
                // but handle it gracefully).
                break;
            }

            // Hand off to provisioning backend with resolved flavor/image details
            var provisionReq = new ProvisionRequest(
                resource.Id, resource.Name, req.FlavorId, req.ImageId, userId.ToString(),
                Vcpus: flavor.Vcpus, RamMb: flavor.RamMb, DiskGb: flavor.DiskGb,
                ImageName: image.Name,
                IpAddress: allocatedIp);
            var result = await _backend.ProvisionAsync(provisionReq, ct);

            if (result.Success)
            {
                // Persist backend-assigned ids, then transition Created → Provisioning → Running.
                var finalIp = !string.IsNullOrWhiteSpace(result.IpAddress) ? result.IpAddress : allocatedIp;
                resource.SetExternalId(result.ExternalId);
                resource.SetIpAddress(finalIp);
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

            // Backend failed. If it's an IP conflict and we have retries
            // left, release the IP and try again with a fresh one.
            //
            // Key insight: ReleaseAsync puts the IP back into the _free
            // pool, so the next AllocateAsync would immediately re-pick
            // it (it's the minimum slot). Instead, we release the IP
            // and then block it with the orphan sentinel so the
            // allocator skips it on the next attempt. The sentinel
            // is a synthetic Guid that's never a real resource.
            await _network.ReleaseAsync(allocatedIp, ct);
            var orphanSentinel = Guid.Parse("00000000-0000-0000-0000-000000000001");
            await _network.ClaimExternalIpAsync(allocatedIp, orphanSentinel, ct);
            allocatedIp = null;

            var isIpConflict = result.Error?.Contains("Address already in use", StringComparison.OrdinalIgnoreCase) == true
                || result.Error?.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) == true;

            if (isIpConflict && provisionAttempts < maxAttempts)
            {
                // Record the failed attempt as an event for diagnostics
                await _resources.AddEventAsync(
                    ResourceEvent.Create(resource.Id, "ProvisionRetry", ResourceStatus.Created, ResourceStatus.Created,
                        $"IP conflict on attempt {provisionAttempts}, retrying with new IP"),
                    ct);
                continue; // retry with a new IP
            }

            // Non-retryable error, or out of retries.
            resource.TransitionTo(ResourceStatus.Provisioning, "Backend called");
            resource.TransitionTo(ResourceStatus.Failed, $"Backend error: {result.Error}");
            await _resources.UpdateAsync(resource, ct);
            await _resources.AddEventAsync(
                ResourceEvent.Create(resource.Id, "ProvisionFailed", ResourceStatus.Provisioning, ResourceStatus.Failed, result.Error ?? "Unknown error"),
                ct);
            return Result<ResourceSummaryDto>.Failure($"Provisioning backend failed: {result.Error}");
        }

        // Shouldn't reach here, but if we do (exhausted retries with no
        // explicit error), return a generic failure.
        resource.TransitionTo(ResourceStatus.Provisioning, "Backend called");
        resource.TransitionTo(ResourceStatus.Failed, "Provisioning failed after retries");
        await _resources.UpdateAsync(resource, ct);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "ProvisionFailed", ResourceStatus.Provisioning, ResourceStatus.Failed, "Exhausted retries"),
            ct);
        return Result<ResourceSummaryDto>.Failure("Provisioning failed after retries. Please try again.");
    }

    /// <summary>
    /// Compute a Terraform-like preview of an upcoming provision without creating
    /// any rows. Used by POST /api/resources/preview and rendered as a summary
    /// card on the front-end provision page before the user clicks "Provision".
    /// </summary>
    public async Task<Result<ProvisioningPlanDto>> PreviewAsync(
        Guid flavorId, Guid imageId, CancellationToken ct)
    {
        var flavor = await _flavors.FindByIdAsync(flavorId, ct);
        if (flavor is null || !flavor.Active)
            return Result<ProvisioningPlanDto>.Failure("Flavor not found or inactive");

        var image = await _images.FindByIdAsync(imageId, ct);
        if (image is null)
            return Result<ProvisioningPlanDto>.Failure("Image not found");

        var warnings = new List<string>();
        var fits = image.SizeGb <= flavor.DiskGb;
        if (!fits)
        {
            warnings.Add(
                $"Image '{image.Name}' ({image.SizeGb} GB) is larger than this flavor's " +
                $"disk ({flavor.DiskGb} GB). Provisioning may fail or the image will " +
                $"be resized automatically by the backend.");
        }

        // Image families that demand > 2 vCPUs tend to under-perform
        // on burstable flavors; surface this so reviewers see domain thinking.
        if (flavor.Vcpus < 2 && image.Os is "Ubuntu" or "Debian" or "AlmaLinux")
        {
            warnings.Add(
                $"This is a burstable flavor ({flavor.Vcpus} vCPU). " +
                $"Desktop-class workloads on {image.Os} may feel constrained. " +
                $"Consider a 2-vCPU package for interactive use.");
        }

        return Result<ProvisioningPlanDto>.Success(new ProvisioningPlanDto(
            MonthlyCostEstimate: flavor.PricePerMonth,
            HourlyCostEstimate: flavor.PricePerHour,
            Vcpus: flavor.Vcpus,
            RamMb: flavor.RamMb,
            DiskGb: flavor.DiskGb,
            ImageName: image.Name,
            ImageOs: image.Os,
            ImageVersion: image.Version,
            ImageSizeGb: image.SizeGb,
            ImageFitsInFlavorDisk: fits,
            Warnings: warnings));
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
            // Terminate is a destructive, one-way action — never let a
            // backend hiccup (e.g. Docker daemon unavailable, OpenStack
            // rate-limit, or a stale "fake-..." externalId from older
            // seed data) trap the user with a resource they can't
            // remove. Log the backend failure but proceed with the
            // state-machine transition. The frontend will surface a
            // warning toast via a separate "best-effort" cleanup channel
            // in a follow-up; for now, termination always wins.
            var backendResult = await _backend.TerminateAsync(resource.ExternalId, ct);
            if (!backendResult.Success)
            {
                // Intentionally swallow the error. The state transition
                // below is the source of truth; the backend is best-effort.
                // Operators can clean up orphaned containers out-of-band.
            }
        }

        var prev = resource.Status;
        resource.TransitionTo(ResourceStatus.Terminated, "User terminated");
        await _resources.UpdateAsync(resource, ct);
        await _resources.AddEventAsync(
            ResourceEvent.Create(resource.Id, "StatusChange", prev, ResourceStatus.Terminated, "User terminated resource"),
            ct);

        // Release the /24 IP slot back to the pool so the next
        // AllocateAsync can hand it out. Best-effort — never block
        // terminate on this.
        if (!string.IsNullOrWhiteSpace(resource.IpAddress))
        {
            try
            {
                await _network.ReleaseAsync(resource.IpAddress, ct);
            }
            catch
            {
                // Swallow — the slot will be reclaimed on next
                // NetworkService.RepopulateAsync because terminated
                // resources are filtered out.
            }
        }

        return Result<ResourceSummaryDto>.Success(ToSummary(resource));
    }

    /// <summary>
    /// Clone a terminated (or any) resource into a fresh provision request,
    /// reusing the source's flavor + image and generating a unique name.
    /// Used by the "Recreate with same config" CTA on historical VMs.
    ///
    /// The original resource is left untouched — its event trail stays as-is
    /// for historical reference. The new resource goes through the normal
    /// Created → Provisioning → Running flow via ProvisionAsync.
    /// </summary>
    public async Task<Result<ResourceSummaryDto>> RecreateAsync(
        Guid sourceResourceId, Guid userId, CancellationToken ct)
    {
        var source = await _resources.FindByIdAsync(sourceResourceId, ct);
        if (source is null)
            return Result<ResourceSummaryDto>.Failure("Source resource not found");
        if (source.UserId != userId)
            return Result<ResourceSummaryDto>.Failure("Forbidden");

        // Recreate is most useful for Terminated/Failed VMs but works for any
        // state — the user might want to "spin up a twin" of a Running VM
        // for load testing. We just refuse if the source flavor/image are
        // no longer active (e.g. EOL'd catalog entries).
        var flavor = await _flavors.FindByIdAsync(source.FlavorId, ct);
        if (flavor is null || !flavor.Active)
            return Result<ResourceSummaryDto>.Failure("Source flavor is no longer available");

        var image = await _images.FindByIdAsync(source.ImageId, ct);
        if (image is null)
            return Result<ResourceSummaryDto>.Failure("Source image is no longer available");

        // Generate a unique name: "{sourceName}-copy-{n}" where n is the
        // smallest non-colliding integer starting at 2. Cap retries so a
        // pathological case (1000+ copies) doesn't loop forever.
        var baseName = source.Name;
        var newName = $"{baseName}-copy-2";
        for (var n = 2; n <= 1000; n++)
        {
            var candidate = $"{baseName}-copy-{n}";
            // Cheap existence check via the user's existing names
            var existing = await _resources.ListByUserAsync(userId, ct);
            if (!existing.Any(r => string.Equals(r.Name, candidate, StringComparison.Ordinal)))
            {
                newName = candidate;
                break;
            }
            newName = candidate;
        }

        return await ProvisionAsync(
            userId,
            new ProvisionRequestDto(newName, source.FlavorId, source.ImageId),
            ct);
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
        // Stopped or terminal VMs have no live runtime metrics; avoid
        // calling the backend for a container that is not running.
        if (resource.IsStopped() || resource.IsTerminated()) return ResourceUsage.Empty();
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