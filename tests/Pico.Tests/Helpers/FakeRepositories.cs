using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain.Entities;

namespace Pico.Tests.Helpers;

/// <summary>In-memory fake for IResourceRepository — drives ResourceService tests.</summary>
public class FakeResourceRepository : IResourceRepository
{
    public Dictionary<Guid, Resource> Resources { get; } = new();
    public Dictionary<Guid, Resource> ById => Resources;
    public Dictionary<Guid, List<ResourceEvent>> Events { get; } = new();

    public Task<Resource?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<Resource?>(Resources.GetValueOrDefault(id));

    public Task<IReadOnlyList<Resource>> ListByUserAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Resource>>(Resources.Values.Where(r => r.UserId == userId).ToList());

    public Task<IReadOnlyList<Resource>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Resource>>(Resources.Values.ToList());

    public Task<IReadOnlyList<Resource>> ListActiveByUserAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Resource>>(Resources.Values
            .Where(r => r.UserId == userId && !r.IsTerminated()).ToList());

    public Task AddAsync(Resource resource, CancellationToken ct)
    {
        Resources[resource.Id] = resource;
        Events[resource.Id] = new List<ResourceEvent>();
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Resource resource, CancellationToken ct) { Resources[resource.Id] = resource; return Task.CompletedTask; }

    public Task AddEventAsync(ResourceEvent evt, CancellationToken ct)
    {
        Events.GetOrAdd(evt.ResourceId, _ => new()).Add(evt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ResourceEvent>> ListEventsAsync(Guid resourceId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResourceEvent>>(Events.GetValueOrDefault(resourceId, new()));
}

static class DictExt
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> factory) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var v))
            dict[key] = v = factory(key);
        return v;
    }
}

/// <summary>Fake flavor repository for tests.</summary>
public class FakeFlavorRepository : IFlavorRepository
{
    public Dictionary<Guid, Flavor> Flavors { get; } = new();
    public Task<Flavor?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Flavors.TryGetValue(id, out var f) ? f : null);
    public Task<IReadOnlyList<Flavor>> ListActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Flavor>>(Flavors.Values.Where(f => f.Active).OrderBy(f => f.PricePerHour).ToList());
    public Task<IReadOnlyList<Flavor>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Flavor>>(Flavors.Values.OrderBy(f => f.PricePerHour).ToList());
    public Task AddAsync(Flavor flavor, CancellationToken ct) { Flavors[flavor.Id] = flavor; return Task.CompletedTask; }
}

/// <summary>Fake image repository for tests.</summary>
public class FakeImageRepository : IImageRepository
{
    public Dictionary<Guid, Image> Images { get; } = new();
    public Task<Image?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Images.TryGetValue(id, out var i) ? i : null);
    public Task<IReadOnlyList<Image>> ListActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Image>>(Images.Values.OrderBy(i => i.Name).ToList());
    public Task AddAsync(Image image, CancellationToken ct) { Images[image.Id] = image; return Task.CompletedTask; }
}

/// <summary>Fake provisioning backend — controllable success/fail for tests.</summary>
public class FakeProvisioningBackend : IProvisioningBackend
{
    public string Mode => "fake";
    public bool ProvisionShouldFail { get; set; }
    public bool StartShouldFail { get; set; }

    public string ProvisionedExternalIdFormat(Guid id) => $"fake-{id:N}";

    public Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct)
    {
        if (ProvisionShouldFail)
            return Task.FromResult(ProvisionResult.Fail("Backend declined"));
        return Task.FromResult(ProvisionResult.Ok(
            ProvisionedExternalIdFormat(request.ResourceId), "10.0.0.42"));
    }

    public Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct)
    {
        if (StartShouldFail)
            return Task.FromResult(ProvisionResult.Fail("Start failed"));
        return Task.FromResult(ProvisionResult.Ok(externalId, "10.0.0.42"));
    }

    public Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct) =>
        Task.FromResult(ProvisionResult.Ok(externalId, "10.0.0.42"));

    public Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct) =>
        Task.FromResult(ProvisionResult.Ok(externalId, "10.0.0.42"));

    public Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct) =>
        Task.FromResult(new ResourceUsage(42.5, 512, 100, 1024, 2048, DateTimeOffset.UtcNow));

    public Task<BackendHealth> GetHealthAsync(CancellationToken ct) =>
        Task.FromResult(new BackendHealth(Mode, true, null, DateTimeOffset.UtcNow));
}
