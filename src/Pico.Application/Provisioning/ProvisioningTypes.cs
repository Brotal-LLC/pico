using Pico.Domain.Entities;

namespace Pico.Application.Provisioning;

/// <summary>Request to provision a new resource.</summary>
public record ProvisionRequest(
    Guid ResourceId,
    string Name,
    Guid FlavorId,
    Guid ImageId,
    string UserId
);

/// <summary>Result of a provisioning backend operation.</summary>
public record ProvisionResult(
    bool Success,
    string? ExternalId,
    string? IpAddress,
    string? Error
)
{
    public static ProvisionResult Ok(string externalId, string ipAddress) =>
        new(true, externalId, ipAddress, null);
    public static ProvisionResult Fail(string error) =>
        new(false, null, null, error);
}

/// <summary>Resource usage snapshot (CPU%, RAM MB used, disk I/O, network bytes).</summary>
public record ResourceUsage(
    double CpuPercent,
    double RamMbUsed,
    int DiskIoKbps,
    long NetworkBytesIn,
    long NetworkBytesOut,
    DateTimeOffset SampledAt
)
{
    public static ResourceUsage Empty() =>
        new(0, 0, 0, 0, 0, DateTimeOffset.UtcNow);
}

/// <summary>Backend health snapshot for /api/health.</summary>
public record BackendHealth(
    string Mode,           // "mock" | "docker" | "openstack"
    bool Healthy,
    string? Message,
    DateTimeOffset CheckedAt
);
