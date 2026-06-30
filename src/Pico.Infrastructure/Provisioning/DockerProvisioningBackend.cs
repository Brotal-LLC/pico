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
        _docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock"))
            .CreateClient();
    }

    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct)
    {
        try
        {
            // Create container with placeholder — flavor-based limits applied in API layer
            // (we don't fetch the flavor here to keep the backend pluggable)
            var createParams = new CreateContainerParameters
            {
                Image = "alpine:3.19",
                Name = $"pico-{request.Name}-{Guid.NewGuid():N}".Substring(0, 64),
                Cmd = new List<string> { "sleep", "infinity" },
                HostConfig = new HostConfig
                {
                    Memory = 1_073_741_824,    // 1 GB default
                    NanoCPUs = 1_000_000_000,  // 1 vCPU default
                    AutoRemove = false,
                },
            };

            var response = await _docker.Containers.CreateContainerAsync(createParams, ct);
            await _docker.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), ct);

            // Inspect to get IP
            var inspect = await _docker.Containers.InspectContainerAsync(response.ID, ct);
            var ip = inspect.NetworkSettings.IPAddress ?? "127.0.0.1";

            return ProvisionResult.Ok(response.ID, ip);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Docker provisioning failed");
            return ProvisionResult.Fail(ex.Message);
        }
    }

    public async Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct)
    {
        try
        {
            await _docker.Containers.StartContainerAsync(externalId, new ContainerStartParameters(), ct);
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex)
        {
            return ProvisionResult.Fail(ex.Message);
        }
    }

    public async Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct)
    {
        try
        {
            await _docker.Containers.StopContainerAsync(externalId, new ContainerStopParameters(), ct);
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex)
        {
            return ProvisionResult.Fail(ex.Message);
        }
    }

    public async Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct)
    {
        try
        {
            await _docker.Containers.RemoveContainerAsync(externalId,
                new ContainerRemoveParameters { Force = true }, ct);
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex)
        {
            return ProvisionResult.Fail(ex.Message);
        }
    }

    public async Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct)
    {
        try
        {
            var stats = await _docker.Containers.GetContainerStatsAsync(externalId, new ContainerStatsParameters(), ct);
            // The Docker.DotNet 3.125.15 API returns Stream; deserialize via System.Text.Json
            ContainerStatsResponse? parsed = null;
            if (stats is Stream s)
            {
                using var reader = new StreamReader(s);
                var json = await reader.ReadToEndAsync(ct);
                parsed = System.Text.Json.JsonSerializer.Deserialize<ContainerStatsResponse>(json);
            }
            if (parsed is null) return ResourceUsage.Empty();

            var cpuDelta = parsed.CPUStats.CPUUsage.TotalUsage - parsed.PreCPUStats.CPUUsage.TotalUsage;
            var systemDelta = parsed.CPUStats.SystemUsage - parsed.PreCPUStats.SystemUsage;
            var cpuPct = systemDelta > 0 ? Math.Round(100.0 * cpuDelta / systemDelta, 1) : 0.0;
            var ramMb = parsed.MemoryStats.Usage / 1024.0 / 1024.0;
            return new ResourceUsage(cpuPct, ramMb, 0, 0, 0, DateTimeOffset.UtcNow);
        }
        catch
        {
            return ResourceUsage.Empty();
        }
    }

    public Task<BackendHealth> GetHealthAsync(CancellationToken ct)
    {
        try
        {
            var version = _docker.System.GetSystemInfoAsync().GetAwaiter().GetResult();
            return Task.FromResult(new BackendHealth("docker", true, $"Docker {version.ServerVersion}", DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new BackendHealth("docker", false, ex.Message, DateTimeOffset.UtcNow));
        }
    }
}
