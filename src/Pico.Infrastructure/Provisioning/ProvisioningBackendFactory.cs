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
    private readonly MockProvisioningBackend _mock;
    private readonly Lazy<DockerProvisioningBackend> _docker;
    private readonly Lazy<OpenStackProvisioningBackend> _openstack;

    public ProvisioningBackendFactory(
        MockProvisioningBackend mock,
        Lazy<DockerProvisioningBackend> docker,
        Lazy<OpenStackProvisioningBackend> openstack)
    {
        _mock = mock;
        _docker = docker;
        _openstack = openstack;
    }

    public IProvisioningBackend Resolve(string? mode)
    {
        return (mode ?? "mock").ToLowerInvariant() switch
        {
            "docker" => _docker.Value,
            "openstack" => _openstack.Value,
            _ => _mock,  // default: mock
        };
    }
}
