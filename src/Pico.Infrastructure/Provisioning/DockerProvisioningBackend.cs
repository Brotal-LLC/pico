using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Pico.Application.Provisioning;

namespace Pico.Infrastructure.Provisioning;

/// <summary>
/// Real Docker provisioning: creates containers as "VMs" with CPU/RAM limits
/// matching the selected flavor. Used when PROVISIONING_MODE=docker.
/// Mounts /var/run/docker.sock to reach the host Docker daemon.
/// </summary>
public class DockerProvisioningBackend : IProvisioningBackend
{
    public string Mode => "docker";

    private readonly DockerClient _docker;
    private readonly ILogger<DockerProvisioningBackend> _logger;

    public DockerProvisioningBackend(ILogger<DockerProvisioningBackend> logger)
    {
        _logger = logger;
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "unix:///var/run/docker.sock";
        _docker = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();
    }

    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct)
    {
        try
        {
            // Map Pico image name to Docker image
            var dockerImage = MapImageName(request.ImageName);

            var containerName = $"pico-{request.Name}-{Guid.NewGuid():N}";
            if (containerName.Length > 64)
                containerName = containerName[..64];

            var createParams = new CreateContainerParameters
            {
                Image = dockerImage,
                Name = containerName,
                Cmd = new List<string> { "sleep", "infinity" },
                HostConfig = new HostConfig
                {
                    Memory = request.RamMb * 1024L * 1024L,    // MB → bytes
                    NanoCPUs = request.Vcpus * 1_000_000_000L,  // vCPUs → nanocpus
                    AutoRemove = false,
                },
            };

            // If the orchestrator pre-allocated an IP from the /24 pool,
            // attach the container to pico-vm-net with that exact IP.
            // This is the new happy path; without it, we fall back to
            // Docker's IPAM (which returns whatever the bridge picked).
            if (!string.IsNullOrWhiteSpace(request.IpAddress))
            {
                await EnsureNetworkAsync(VM_NETWORK, ct);
                createParams.NetworkingConfig = new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [VM_NETWORK] = new EndpointSettings
                        {
                            IPAMConfig = new EndpointIPAMConfig { IPv4Address = request.IpAddress }
                        }
                    }
                };
            }

            var response = await _docker.Containers.CreateContainerAsync(createParams, ct);
            await _docker.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), ct);

            var inspect = await _docker.Containers.InspectContainerAsync(response.ID, ct);
            // The DockerDotNet deprecated NetworkSettings.IPAddress field
            // races against attach completion and frequently returns "".
            // Prefer the per-endpoint IP for pico-vm-net, fall back to the
            // legacy single-network field, then to the orchestrator's IP.
            var ip = ResolveAssignedIp(inspect, request.IpAddress);

            return ProvisionResult.Ok(response.ID, ip);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Docker provisioning failed");
            return ProvisionResult.Fail(ex.Message);
        }
    }

    /// <summary>The Docker bridge Pico assigns VM containers to.</summary>
    public const string VM_NETWORK = "pico-vm-net";

    /// <summary>
    /// Idempotently create the pico-vm-net bridge with a deterministic
    /// /24 subnet. Called lazily by ProvisionAsync when an orchestrator-
    /// allocated IP needs to be assigned to a container.
    /// </summary>
    private async Task EnsureNetworkAsync(string name, CancellationToken ct)
    {
        var existing = await _docker.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [name] = true }
                }
            }, ct);
        if (existing.Any(n => string.Equals(n.Name, name, StringComparison.Ordinal))) return;

        await _docker.Networks.CreateNetworkAsync(new NetworksCreateParameters
        {
            Name = name,
            Driver = "bridge",
            IPAM = new IPAM
            {
                Config = new List<IPAMConfig>
                {
                    new() { Subnet = "10.42.0.0/24", Gateway = "10.42.0.1" }
                }
            }
        }, ct);
        _logger.LogInformation("Created Docker network {Network} with subnet 10.42.0.0/24", name);
    }

    /// <summary>
    /// Resolve the container's assigned IP, preferring the per-endpoint
    /// view of pico-vm-net (which is current), then the legacy single-
    /// network field, then the orchestrator-supplied fallback.
    /// </summary>
    private static string ResolveAssignedIp(ContainerInspectResponse inspect, string? fallback)
    {
        if (inspect.NetworkSettings?.Networks is { } networks)
        {
            if (networks.TryGetValue(VM_NETWORK, out var vmNet) && !string.IsNullOrWhiteSpace(vmNet.IPAddress))
                return vmNet.IPAddress;
            // First non-empty entry, ordered by name for determinism.
            foreach (var kv in networks.OrderBy(kv => kv.Key))
            {
                if (!string.IsNullOrWhiteSpace(kv.Value?.IPAddress))
                    return kv.Value.IPAddress;
            }
        }
        if (!string.IsNullOrWhiteSpace(inspect.NetworkSettings?.IPAddress))
            return inspect.NetworkSettings.IPAddress;
        return fallback ?? "127.0.0.1";
    }

    public async Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct)
    {
        try
        {
            await _docker.Containers.StartContainerAsync(externalId, new ContainerStartParameters(), ct);
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex) { return ProvisionResult.Fail(ex.Message); }
    }

    public async Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct)
    {
        try
        {
            await _docker.Containers.StopContainerAsync(externalId, new ContainerStopParameters(), ct);
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex) { return ProvisionResult.Fail(ex.Message); }
    }

    public async Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct)
    {
        try
        {
            await _docker.Containers.RemoveContainerAsync(externalId,
                new ContainerRemoveParameters { Force = true }, ct);
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex) { return ProvisionResult.Fail(ex.Message); }
    }

    public async Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct)
    {
        try
        {
            var stats = await _docker.Containers.GetContainerStatsAsync(externalId,
                new ContainerStatsParameters { Stream = false }, ct);

            if (stats is Stream s)
            {
                using var reader = new StreamReader(s);
                var json = await reader.ReadToEndAsync(ct);
                var parsed = System.Text.Json.JsonSerializer.Deserialize<ContainerStatsResponse>(json);
                if (parsed is null) return ResourceUsage.Empty();

                var cpuDelta = parsed.CPUStats.CPUUsage.TotalUsage - parsed.PreCPUStats.CPUUsage.TotalUsage;
                var systemDelta = parsed.CPUStats.SystemUsage - parsed.PreCPUStats.SystemUsage;
                var cpuPct = systemDelta > 0 ? Math.Round(100.0 * cpuDelta / systemDelta, 1) : 0.0;
                var ramMb = parsed.MemoryStats.Usage / 1024.0 / 1024.0;
                return new ResourceUsage(cpuPct, ramMb, 0, 0, 0, DateTimeOffset.UtcNow);
            }
            return ResourceUsage.Empty();
        }
        catch
        {
            return ResourceUsage.Empty();
        }
    }

    public async Task<BackendHealth> GetHealthAsync(CancellationToken ct)
    {
        try
        {
            var info = await _docker.System.GetSystemInfoAsync(ct);
            return new BackendHealth("docker", true, $"Docker {info.ServerVersion}", DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new BackendHealth("docker", false, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>Map Pico image names to Docker image references.</summary>
    private static string MapImageName(string picoImage) => picoImage switch
    {
        "ubuntu-22" => "ubuntu:22.04",
        "ubuntu-24" => "ubuntu:24.04",
        "debian-12" => "debian:12",
        "alma-9" => "almalinux:9",
        _ => "alpine:3.19",  // safe default
    };
}