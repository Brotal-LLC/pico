using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pico.Application.Common;
using Pico.Application.Networking;
using Pico.Domain.Enums;
using Pico.Infrastructure.Provisioning;

namespace Pico.Infrastructure.Networking;

/// <summary>
/// IHostedService that reconciles the in-memory <see cref="NetworkService"/>
/// IP pool with the actual state of the <c>pico-vm-net</c> Docker bridge.
///
/// Problem: <see cref="NetworkBootstrapper"/> hydrates from the DB, but the
/// DB only knows about resources that completed provisioning. If a provision
/// crashed mid-flight (container created on the network but the API process
/// died before persisting the IP), or if someone manually <c>docker run</c>s
/// a container onto pico-vm-net, the allocator thinks that IP is free and
/// hands it to the next provision — Docker then rejects with 403 Forbidden
/// "Address already in use".
///
/// This service runs after <see cref="NetworkBootstrapper"/> and scans the
/// live Docker network. For each container IP:
///   • If the DB resource already owns it → no-op.
///   • If no DB resource owns it → claim it with a synthetic Guid so the
///     allocator skips it. (The container is an orphan; an operator can
///     clean it up out-of-band.)
///   • If a Terminated/Failed DB resource owns it but a live container is
///     still using it → force-claim it to the synthetic Guid so the
///     allocator doesn't hand it out again.
///
/// Only runs when PROVISIONING_MODE=docker. In mock/openstack mode the
/// service is a no-op (the Docker client may not even be reachable).
/// </summary>
public class DockerNetworkReconciler : IHostedService
{
    // Synthetic resource ID used to claim orphan IPs. Not a real resource —
    // just a sentinel so the allocator's _owner map blocks the slot.
    private static readonly Guid OrphanSentinelId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly NetworkService _network;
    private readonly IServiceProvider _services;
    private readonly ILogger<DockerNetworkReconciler> _logger;
    private readonly string _provisioningMode;

    public DockerNetworkReconciler(
        NetworkService network,
        IServiceProvider services,
        ILogger<DockerNetworkReconciler> logger)
    {
        _network = network;
        _services = services;
        _logger = logger;
        _provisioningMode = Environment.GetEnvironmentVariable("PROVISIONING_MODE") ?? "mock";
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!string.Equals(_provisioningMode, "docker", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping Docker network reconciliation (mode={Mode})", _provisioningMode);
            return;
        }

        try
        {
            await ReconcileAsync(ct);
        }
        catch (Exception ex)
        {
            // Never crash boot on reconciliation failure. The allocator
            // will still work — it just might hand out an IP that's in
            // use, which the Docker API will reject. The retry logic in
            // ResourceService will handle that case.
            _logger.LogWarning(ex,
                "Docker network reconciliation failed. Continuing without it; " +
                "provisioning retries will handle any IP conflicts.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task ReconcileAsync(CancellationToken ct)
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST")
            ?? "unix:///var/run/docker.sock";
        var docker = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();

        // List all networks and find pico-vm-net
        var networks = await docker.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [DockerProvisioningBackend.VM_NETWORK] = true }
                }
            }, ct);

        var vmNet = networks.FirstOrDefault(n =>
            string.Equals(n.Name, DockerProvisioningBackend.VM_NETWORK, StringComparison.Ordinal));

        if (vmNet is null)
        {
            _logger.LogInformation("Docker network {Network} not found — nothing to reconcile",
                DockerProvisioningBackend.VM_NETWORK);
            return;
        }

        // Inspect the network to get container→IP mappings.
        // Use the network name (which is the same as the ID for our purposes).
        var inspect = await docker.Networks.InspectNetworkAsync(vmNet.Name, ct);
        var containers = inspect?.Containers;
        if (containers is null || containers.Count == 0)
        {
            _logger.LogInformation("Docker network {Network} has no containers — nothing to reconcile",
                DockerProvisioningBackend.VM_NETWORK);
            return;
        }

        // Fetch all non-terminated resources from the DB to build an
        // IP → resourceId lookup.
        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IResourceRepository>();
        var allResources = await repo.ListAllAsync(ct);
        var ipToResource = allResources
            .Where(r => !r.IsTerminated() && !string.IsNullOrWhiteSpace(r.IpAddress))
            .ToDictionary(r => r.IpAddress!, r => r.Id);

        int claimed = 0, confirmed = 0, forceClaimed = 0;

        foreach (var entry in containers)
        {
            var info = entry.Value;
            // EndpointResource uses IPv4Address (not IPAddress like EndpointSettings)
            var ip = info.IPv4Address;
            if (string.IsNullOrWhiteSpace(ip))
            {
                // Container is on the network but has no IP (e.g. "created" state).
                continue;
            }

            // Normalize: Docker returns "10.42.0.2/24", we want "10.42.0.2"
            ip = ip.Split('/')[0];

            if (ipToResource.TryGetValue(ip, out var resourceId))
            {
                // DB knows about this IP — already claimed by NetworkBootstrapper.
                confirmed++;
                continue;
            }

            // No DB resource owns this IP. Check if a terminated resource
            // had it (stale allocation from before a restart).
            var terminatedOwner = allResources
                .FirstOrDefault(r => r.IsTerminated() && r.IpAddress == ip);

            if (terminatedOwner is not null)
            {
                // Force-claim: the old owner is terminated but a live
                // container is still using the IP. Evict the stale owner.
                await _network.ForceClaimExternalIpAsync(ip, OrphanSentinelId, ct);
                forceClaimed++;
                _logger.LogInformation(
                    "Force-claimed IP {Ip} from terminated resource {ResourceId} " +
                    "(container {Container} is still using it)",
                    ip, terminatedOwner.Id, info.Name);
            }
            else
            {
                // Completely orphaned — no DB resource ever owned this IP.
                // Claim it with the sentinel so the allocator skips it.
                var claimedOk = await _network.ClaimExternalIpAsync(ip, OrphanSentinelId, ct);
                if (claimedOk)
                {
                    claimed++;
                    _logger.LogWarning(
                        "Claimed orphan IP {Ip} on pico-vm-net (container {Container} " +
                        "has no matching DB resource). Operator should remove this container.",
                        ip, info.Name);
                }
            }
        }

        _logger.LogInformation(
            "Docker network reconciliation complete: {Confirmed} confirmed, " +
            "{Claimed} orphan-claimed, {ForceClaimed} force-claimed",
            confirmed, claimed, forceClaimed);
    }
}