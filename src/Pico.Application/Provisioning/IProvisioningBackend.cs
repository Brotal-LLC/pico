namespace Pico.Application.Provisioning;

/// <summary>
/// Pluggable interface for resource provisioning. Three implementations:
/// - MockProvisioningBackend (DB-only, no external deps)
/// - DockerProvisioningBackend (creates real Docker containers)
/// - OpenStackProvisioningBackend (calls Nova API)
/// The mode is selected at startup via PROVISIONING_MODE env var.
/// </summary>
public interface IProvisioningBackend
{
    /// <summary>Backend identifier shown in /api/health.</summary>
    string Mode { get; }

    Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct);
    Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct);
    Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct);
    Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct);
    Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct);
    Task<BackendHealth> GetHealthAsync(CancellationToken ct);
}
