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
        // Honor the orchestrator-allocated /24 IP if present; fall back
        // to a random address for callers that haven't wired the
        // NetworkService allocator (legacy tests, OpenStack mode).
        var ip = !string.IsNullOrWhiteSpace(request.IpAddress)
            ? request.IpAddress
            : $"10.{_rng.Next(1, 254)}.{_rng.Next(1, 254)}.{_rng.Next(1, 254)}";
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

    public Task<IShellSession> ExecInteractiveAsync(string externalId, CancellationToken ct)
    {
        // Mock has no real VM to shell into. Return a session that
        // immediately writes a one-line notice then signals EOF — the
        // browser's terminal panel displays the message and then closes.
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "Shell access is not available for mock-provisioned VMs.\r\n" +
            "Switch PROVISIONING_MODE to 'docker' or 'openstack' to enable the shell panel.\r\n");
        return Task.FromResult<IShellSession>(new PrefilledEofSession(bytes));
    }

    private sealed class PrefilledEofSession : IShellSession
    {
        private readonly byte[] _prelude;
        private int _sent;
        private readonly Stream _input = new MemoryStream(Array.Empty<byte>());
        public PrefilledEofSession(byte[] prelude) => _prelude = prelude;
        public Stream InputStream => _input;
        public Stream OutputStream => new OneShotReadStream(this);
        public void Kill() => DisposeAsync().AsTask().GetAwaiter().GetResult();
        public ValueTask DisposeAsync() { _input.Dispose(); return ValueTask.CompletedTask; }

        private sealed class OneShotReadStream : Stream
        {
            private readonly PrefilledEofSession _owner;
            public OneShotReadStream(PrefilledEofSession o) => _owner = o;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _owner._prelude.Length;
            public override long Position { get => _owner._sent; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
            public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_owner._sent >= _owner._prelude.Length) return 0;
                var n = Math.Min(count, _owner._prelude.Length - _owner._sent);
                Buffer.BlockCopy(_owner._prelude, _owner._sent, buffer, offset, n);
                _owner._sent += n;
                return n;
            }
        }
    }
}
