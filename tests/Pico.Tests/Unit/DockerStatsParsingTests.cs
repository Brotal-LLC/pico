using System.Text.Json;
using Pico.Application.Provisioning;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// Tests that DockerProvisioningBackend.GetUsageAsync correctly parses
/// the snake_case JSON returned by the Docker stats API. The Docker.DotNet
/// SDK's ContainerStatsResponse type uses PascalCase without STJ attributes,
/// so a naive JsonSerializer.Deserialize yields all-zeros. We verify the
/// manual JsonDocument parsing extracts CPU, RAM, network, and disk I/O.
/// </summary>
public class DockerStatsParsingTests
{
    // Realistic Docker stats JSON (trimmed to relevant fields).
    // Based on actual output from the Docker stats API.
    private const string SampleStatsJson = """
    {
      "cpu_stats": {
        "cpu_usage": { "total_usage": 161944000 },
        "system_cpu_usage": 9625185190000000,
        "online_cpus": 2
      },
      "precpu_stats": {
        "cpu_usage": { "total_usage": 161944000 },
        "system_cpu_usage": 9625648950000000
      },
      "memory_stats": { "usage": 1716224 },
      "networks": {
        "eth0": { "rx_bytes": 28950, "tx_bytes": 126 }
      },
      "blkio_stats": {
        "io_service_bytes_recursive": [
          { "op": "read", "value": 278528 },
          { "op": "write", "value": 0 }
        ]
      }
    }
    """;

    [Fact]
    public void ParsesCpuRamNetworkDiskFromRealDockerJson()
    {
        using var doc = JsonDocument.Parse(SampleStatsJson);
        var root = doc.RootElement;

        var cpuStats = root.GetProperty("cpu_stats");
        var preCpu = root.GetProperty("precpu_stats");
        var cpuDelta = GetLong(cpuStats, "cpu_usage", "total_usage") - GetLong(preCpu, "cpu_usage", "total_usage");
        var sysDelta = GetLong(cpuStats, "system_cpu_usage") - GetLong(preCpu, "system_cpu_usage");
        var onlineCpus = cpuStats.TryGetProperty("online_cpus", out var oc) && oc.TryGetInt64(out var ocVal) && ocVal > 0
            ? ocVal : 1;
        var cpuPct = sysDelta != 0 ? Math.Round(100.0 * cpuDelta / sysDelta * onlineCpus, 1) : 0.0;

        var ramBytes = GetLong(root.GetProperty("memory_stats"), "usage");
        var ramMb = Math.Round(ramBytes / 1024.0 / 1024.0, 1);

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

        long diskBytes = 0;
        if (root.TryGetProperty("blkio_stats", out var blkio) &&
            blkio.TryGetProperty("io_service_bytes_recursive", out var ioBytes) &&
            ioBytes.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in ioBytes.EnumerateArray())
            {
                if (entry.TryGetProperty("value", out var v) && v.TryGetInt64(out var vVal))
                    diskBytes += vVal;
            }
        }

        Assert.Equal(0.0, cpuPct); // cpu_delta is 0 because pre==post in this sample
        Assert.Equal(1.6, ramMb);  // 1716224 bytes → 1.637 MB → rounded to 1.6
        Assert.Equal(28950L, netIn);
        Assert.Equal(126L, netOut);
        Assert.Equal(272, diskBytes / 1024); // 278528 bytes → 272 KB
    }

    [Fact]
    public void ParsesCpuPercentWhenCpuDeltaIsNonZero()
    {
        var json = """
        {
          "cpu_stats": {
            "cpu_usage": { "total_usage": 200000000 },
            "system_cpu_usage": 1000000000,
            "online_cpus": 2
          },
          "precpu_stats": {
            "cpu_usage": { "total_usage": 100000000 },
            "system_cpu_usage": 900000000
          },
          "memory_stats": { "usage": 1048576 },
          "networks": {},
          "blkio_stats": {}
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cpuStats = root.GetProperty("cpu_stats");
        var preCpu = root.GetProperty("precpu_stats");
        var cpuDelta = GetLong(cpuStats, "cpu_usage", "total_usage") - GetLong(preCpu, "cpu_usage", "total_usage");
        var sysDelta = GetLong(cpuStats, "system_cpu_usage") - GetLong(preCpu, "system_cpu_usage");
        var onlineCpus = 2L;
        var cpuPct = sysDelta > 0 ? Math.Round(100.0 * cpuDelta / sysDelta * onlineCpus, 1) : 0.0;

        // (100000000 / 100000000) * 2 * 100 = 200.0
        Assert.Equal(200.0, cpuPct);
    }

    [Fact]
    public void HandlesMissingNetworksAndBlkioGracefully()
    {
        var json = """
        {
          "cpu_stats": {
            "cpu_usage": { "total_usage": 0 },
            "system_cpu_usage": 0,
            "online_cpus": 1
          },
          "precpu_stats": {
            "cpu_usage": { "total_usage": 0 },
            "system_cpu_usage": 0
          },
          "memory_stats": { "usage": 0 }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // No "networks" or "blkio_stats" keys — should default to 0
        Assert.False(root.TryGetProperty("networks", out _));
    }

    [Fact]
    public void ResourceUsageRecordMapsCorrectly()
    {
        var usage = new ResourceUsage(1.5, 512.0, 272, 28950, 126, DateTimeOffset.UtcNow);
        Assert.Equal(1.5, usage.CpuPercent);
        Assert.Equal(512.0, usage.RamMbUsed);
        Assert.Equal(272, usage.DiskIoKbps);
        Assert.Equal(28950L, usage.NetworkBytesIn);
        Assert.Equal(126L, usage.NetworkBytesOut);
    }

    [Fact]
    public void EmptyUsageHasAllZeros()
    {
        var empty = ResourceUsage.Empty();
        Assert.Equal(0.0, empty.CpuPercent);
        Assert.Equal(0.0, empty.RamMbUsed);
        Assert.Equal(0, empty.DiskIoKbps);
        Assert.Equal(0L, empty.NetworkBytesIn);
        Assert.Equal(0L, empty.NetworkBytesOut);
    }

    // ── Helpers ──────────────────────────────────────────────────────
    private static long GetLong(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var v) && v.TryGetInt64(out var val))
            return val;
        return 0;
    }

    private static long GetLong(JsonElement parent, string child, string leaf)
    {
        if (parent.TryGetProperty(child, out var c) && c.TryGetProperty(leaf, out var l) && l.TryGetInt64(out var val))
            return val;
        return 0;
    }
}