using Microsoft.Extensions.DependencyInjection;
using Pico.Application.Provisioning;

namespace Pico.Infrastructure.Provisioning;

/// <summary>
/// Selects the provisioning backend implementation based on PROVISIONING_MODE env var.
/// Modes:
/// - "mock"      → MockProvisioningBackend (default, zero external deps)
/// - "docker"    → DockerProvisioningBackend (creates real Docker containers)
/// - "openstack" → OpenStackProvisioningBackend (calls Nova API on DevStack VM)
/// </summary>
public class ProvisioningBackendFactory
{
    private readonly IServiceProvider _sp;
    private readonly MockProvisioningBackend _mock;

    public ProvisioningBackendFactory(IServiceProvider sp, MockProvisioningBackend mock)
    {
        _sp = sp;
        _mock = mock;
    }

    public IProvisioningBackend Resolve(string? mode)
    {
        return (mode ?? "mock").ToLowerInvariant() switch
        {
            "docker" => _sp.GetRequiredService<DockerProvisioningBackend>(),
            "openstack" => _sp.GetRequiredService<OpenStackProvisioningBackend>(),
            _ => _mock,
        };
    }
}