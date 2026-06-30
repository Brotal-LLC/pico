using Pico.Application.Provisioning;
using Pico.Domain.Entities;

namespace Pico.Infrastructure.Provisioning;

/// <summary>
/// Mock backend: zero external dependencies. Provisions by recording state in the DB,
/// simulating a 2-5s provisioning delay, and generating fake external IDs / IPs.
/// Used when PROVISIONING_MODE=mock — the default for self-contained reviewer runs.
/// </summary>
public class MockProvisioningBackend : IProvisioningBackend
{
    public string Mode => "mock";

    private readonly Random _rng = new();

    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct)
    {
        // Simulate provisioning delay (2-5s)
        var delaySeconds = _rng.Next(2, 6);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);

        var externalId = $"mock-vm-{Guid.NewGuid():N}";
        var ip = $"10.{_rng.Next(1, 254)}.{_rng.Next(1, 254)}.{_rng.Next(1, 254)}";
        return ProvisionResult.Ok(externalId, ip);
    }

    public async Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct)
    {
        await Task.Delay(500, ct);
        return ProvisionResult.Ok(externalId, externalId.Replace("mock-vm-", "10."));
    }

    public async Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct)
    {
        await Task.Delay(500, ct);
        return ProvisionResult.Ok(externalId, "");
    }

    public async Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct)
    {
        await Task.Delay(500, ct);
        return ProvisionResult.Ok(externalId, "");
    }

    public Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct)
    {
        // Realistic-looking random usage
        var usage = new ResourceUsage(
            CpuPercent: Math.Round(_rng.NextDouble() * 100, 1),
            RamMbUsed: Math.Round(_rng.NextDouble() * 4096, 0),
            DiskIoKbps: _rng.Next(0, 10000),
            NetworkBytesIn: _rng.Next(0, int.MaxValue) * 1024L,
            NetworkBytesOut: _rng.Next(0, int.MaxValue) * 1024L,
            SampledAt: DateTimeOffset.UtcNow);
        return Task.FromResult(usage);
    }

    public Task<BackendHealth> GetHealthAsync(CancellationToken ct)
    {
        return Task.FromResult(new BackendHealth(
            Mode: "mock",
            Healthy: true,
            Message: "Mock backend operational — no infrastructure dependencies",
            CheckedAt: DateTimeOffset.UtcNow));
    }
}
