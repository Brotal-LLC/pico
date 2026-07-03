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

                // Docker returns snake_case JSON; Docker.DotNet's
                // ContainerStatsResponse uses PascalCase without
                // [JsonPropertyName] attributes. System.Text.Json is
                // case-sensitive by default, so deserializing into the
                // SDK type yields all-zeros. Parse the raw JSON directly
                // instead — it's a stable, well-documented schema.
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                // ── CPU% ────────────────────────────────────────────
                // Docker formula: (cpu_delta / system_delta) * online_cpus * 100
                var cpuStats = root.GetProperty("cpu_stats");
                var preCpu = root.GetProperty("precpu_stats");
                var cpuDelta = GetUsecpuTotal(cpuStats) - GetUsecpuTotal(preCpu);
                var sysDelta = GetSysCpu(cpuStats) - GetSysCpu(preCpu);
                var onlineCpus = cpuStats.TryGetProperty("online_cpus", out var oc) && oc.TryGetInt64(out var ocVal) && ocVal > 0
                    ? ocVal : 1;
                var cpuPct = sysDelta > 0
                    ? Math.Round(100.0 * cpuDelta / sysDelta * onlineCpus, 1)
                    : 0.0;

                // ── RAM (MB) ────────────────────────────────────────
                var ramBytes = root.GetProperty("memory_stats").TryGetProperty("usage", out var mu) && mu.TryGetInt64(out var memVal)
                    ? memVal : 0L;
                var ramMb = Math.Round(ramBytes / 1024.0 / 1024.0, 1);

                // ── Network bytes ───────────────────────────────────
                // Sum rx_bytes / tx_bytes across all network interfaces.
                long netIn = 0, netOut = 0;
                if (root.TryGetProperty("networks", out var networks))
                {
                    foreach (var net in networks.EnumerateObject())
                    {
                        if (net.Value.TryGetProperty("rx_bytes", out var rx) && rx.TryGetInt64(out var rxVal))
                            netIn += rxVal;
                        if (net.Value.TryGetProperty("tx_bytes", out var tx) && tx.TryGetInt64(out var txVal))
                            netOut += txVal;
                    }
                }

                // ── Disk I/O (KB/s) ─────────────────────────────────
                // blkio_stats.io_service_bytes_recursive: sum read+write
                // values. This is cumulative — convert to KB (not KB/s
                // since we don't track delta between samples yet).
                long diskBytes = 0;
                if (root.TryGetProperty("blkio_stats", out var blkio) &&
                    blkio.TryGetProperty("io_service_bytes_recursive", out var ioBytes) &&
                    ioBytes.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var entry in ioBytes.EnumerateArray())
                    {
                        if (entry.TryGetProperty("value", out var v) && v.TryGetInt64(out var vVal))
                            diskBytes += vVal;
                    }
                }
                var diskKbps = (int)(diskBytes / 1024);

                return new ResourceUsage(cpuPct, ramMb, diskKbps, netIn, netOut, DateTimeOffset.UtcNow);
            }
            return ResourceUsage.Empty();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get container stats for {ExternalId}", externalId);
            return ResourceUsage.Empty();
        }
    }

    private static long GetUsecpuTotal(System.Text.Json.JsonElement cpuStats)
    {
        if (cpuStats.TryGetProperty("cpu_usage", out var cu) &&
            cu.TryGetProperty("total_usage", out var tu) &&
            tu.TryGetInt64(out var val))
            return val;
        return 0;
    }

    private static long GetSysCpu(System.Text.Json.JsonElement cpuStats)
    {
        if (cpuStats.TryGetProperty("system_cpu_usage", out var sc) &&
            sc.TryGetInt64(out var val))
            return val;
        return 0;
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

    public async Task<IShellSession> ExecInteractiveAsync(string externalId, CancellationToken ct)
    {
        // docker exec -i <container> /bin/sh — hijacked bidirectional
        // stream in TTY mode. The session returns the multiplexed
        // stream's input side as the caller's stdin sink and pumps its
        // output side as a readable stream of stdout bytes.
        var exec = await _docker.Exec.ExecCreateContainerAsync(externalId, new ContainerExecCreateParameters
        {
            AttachStdin = true,
            AttachStdout = true,
            AttachStderr = true,
            Tty = true,
            Cmd = new List<string> { "/bin/sh" }
        }, ct);
        var multiplexed = await _docker.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: true, ct);
        return new DockerShellSession(multiplexed);
    }

    /// <summary>
    /// Adapts a Docker <see cref="MultiplexedStream"/> (in TTY mode) to
    /// the <see cref="IShellSession"/> contract. In TTY mode the daemon
    /// sends stdout+stderr already merged and stdin piggybacks on the
    /// same channel, so we can treat the multiplexed stream as a raw
    /// bidirectional pipe: <see cref="InputStream"/> wraps the write
    /// side and <see cref="OutputStream"/> wraps the read side.
    /// </summary>
    private sealed class DockerShellSession : IShellSession
    {
        private readonly MultiplexedStream _mux;
        private readonly DockerShellInputStream _input;
        private readonly DockerShellOutputStream _output;
        private bool _disposed;

        public DockerShellSession(MultiplexedStream mux)
        {
            _mux = mux;
            _input = new DockerShellInputStream(mux);
            _output = new DockerShellOutputStream(mux);
        }

        public Stream InputStream => _input;
        public Stream OutputStream => _output;

        public void Kill()
        {
            // Closing the write side tells the daemon the stdin EOF,
            // which typically causes the shell to exit. If it doesn't,
            // DisposeAsync will dispose the underlying stream and the
            // session ends anyway.
            try { _input.Close(); } catch { }
            try { DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try { _mux.Dispose(); } catch { }
            await Task.CompletedTask;
        }

        private sealed class DockerShellInputStream : Stream
        {
            private readonly MultiplexedStream _mux;
            public DockerShellInputStream(MultiplexedStream mux) => _mux = mux;
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
            public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
            public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => _mux.WriteAsync(b, o, c, CancellationToken.None).GetAwaiter().GetResult();
            public override Task WriteAsync(byte[] b, int o, int c, CancellationToken ct) => _mux.WriteAsync(b, o, c, ct);
        }

        private sealed class DockerShellOutputStream : Stream
        {
            private readonly MultiplexedStream _mux;
            public DockerShellOutputStream(MultiplexedStream mux) => _mux = mux;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
            public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
            public override int Read(byte[] b, int o, int c)
                => ReadAsync(b, o, c, CancellationToken.None).GetAwaiter().GetResult();
            public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct)
            {
                // Docker.DotNet's MultiplexedStream.ReadOutputAsync returns a
                // ReadResult struct (Count + EOF), not a raw int. Adapt.
                return ReadAdaptedAsync(b, o, c, ct);
            }

            private async Task<int> ReadAdaptedAsync(byte[] b, int o, int c, CancellationToken ct)
            {
                var rr = await _mux.ReadOutputAsync(b, o, c, ct);
                return rr.EOF ? 0 : rr.Count;
            }
        }
    }

    /// <summary>Map Pico image names to Docker image references.</summary>
    private static string MapImageName(string picoImage) => picoImage switch
    {
        "ubuntu-22" => "ubuntu:22.04",
        "ubuntu-24" => "ubuntu:24.04",
        "debian-12" => "debian:12",
        "alma-9" => "almalinux:9",
        "" or null => "ubuntu:22.04",  // seeder passes empty string
        _ => "ubuntu:22.04",  // safe default (alpine:3.19 not always pulled)
    };
}