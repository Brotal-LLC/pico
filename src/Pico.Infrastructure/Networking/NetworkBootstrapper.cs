using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pico.Application.Common;
using Pico.Application.Networking;

namespace Pico.Infrastructure.Networking;

/// <summary>
/// IHostedService that hydrates <see cref="NetworkService"/> from the
/// resource repository at API startup. Without this, a fresh API process
/// would happily re-issue IPs that live Docker containers are already
/// using — two VMs would collide on the bridge, and the second one
/// would fail to start with a Docker "address already in use" error.
///
/// This service is intentionally simple: it just delegates to
/// <see cref="NetworkService.RepopulateAsync"/> and logs the result.
/// The Docker network itself (pico-vm-net) is created lazily by
/// <c>DockerProvisioningBackend.EnsureNetworkAsync</c> on first use, so
/// this service has no Docker-mode dependency — it always runs.
/// </summary>
public class NetworkBootstrapper : IHostedService
{
    private readonly NetworkService _network;
    private readonly IResourceRepository? _resources;          // test path
    private readonly IServiceProvider? _resourcesScope;        // production path
    private readonly ILogger<NetworkBootstrapper> _logger;

    /// <summary>
    /// Production ctor: DI injects an IServiceProvider; we resolve the
    /// scoped repository inside StartAsync (which runs at boot, before
    /// any request scope exists).
    /// </summary>
    public NetworkBootstrapper(
        NetworkService network,
        IServiceProvider services,
        ILogger<NetworkBootstrapper> logger)
    {
        _network = network;
        _resourcesScope = services;
        _logger = logger;
    }

    /// <summary>
    /// Test ctor: inject the repository directly. Lets unit tests
    /// exercise the hydration logic without spinning up an
    /// IServiceProvider.
    /// </summary>
    internal NetworkBootstrapper(
        NetworkService network,
        IResourceRepository resources,
        ILogger<NetworkBootstrapper> logger)
    {
        _network = network;
        _resources = resources;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        IResourceRepository repo;
        if (_resources is not null)
        {
            repo = _resources;
        }
        else
        {
            using var scope = _resourcesScope!.CreateScope();
            repo = scope.ServiceProvider.GetRequiredService<IResourceRepository>();
        }
        await RunAsync(ct, repo);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Public for tests — performs the actual hydration.
    /// </summary>
    public Task RunAsync(CancellationToken ct) =>
        RunAsync(ct, _resources
            ?? throw new InvalidOperationException(
                "NetworkBootstrapper.RunAsync requires the test ctor (with a direct repository)."));

    private async Task RunAsync(CancellationToken ct, IResourceRepository repo)
    {
        try
        {
            await _network.RepopulateAsync(repo, ct);
            _logger.LogInformation("VM IP allocator hydrated from existing resources.");
        }
        catch (Exception ex)
        {
            // Don't crash boot on a hydration failure. The allocator
            // will start empty and the first AllocateAsync will hand
            // out a fresh /24 slot. Operators can investigate via logs.
            _logger.LogWarning(ex,
                "NetworkBootstrapper failed to hydrate IP allocator from repository. " +
                "Continuing with an empty pool; first AllocateAsync will scan from 10.42.0.2.");
        }
    }
}