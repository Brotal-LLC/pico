using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain.Entities;

namespace Pico.Tests.Helpers;

/// <summary>In-memory fake for IResourceRepository — drives ResourceService tests.</summary>
public class FakeResourceRepository : IResourceRepository
{
    public Dictionary<Guid, Resource> Resources { get; } = new();
    public Dictionary<Guid, Resource> ById => Resources;
    public Dictionary<Guid, List<ResourceEvent>> Events { get; } = new();

    public Task<Resource?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<Resource?>(Resources.GetValueOrDefault(id));

    public Task<IReadOnlyList<Resource>> ListByUserAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Resource>>(Resources.Values.Where(r => r.UserId == userId).ToList());

    public Task<IReadOnlyList<Resource>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Resource>>(Resources.Values.ToList());

    public Task<IReadOnlyList<Resource>> ListActiveByUserAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Resource>>(Resources.Values
            .Where(r => r.UserId == userId && !r.IsTerminated()).ToList());

    public Task AddAsync(Resource resource, CancellationToken ct)
    {
        Resources[resource.Id] = resource;
        Events[resource.Id] = new List<ResourceEvent>();
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Resource resource, CancellationToken ct) { Resources[resource.Id] = resource; return Task.CompletedTask; }

    public Task AddEventAsync(ResourceEvent evt, CancellationToken ct)
    {
        Events.GetOrAdd(evt.ResourceId, _ => new()).Add(evt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ResourceEvent>> ListEventsAsync(Guid resourceId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResourceEvent>>(Events.GetValueOrDefault(resourceId, new()));
}

static class DictExt
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> factory) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var v))
            dict[key] = v = factory(key);
        return v;
    }
}

/// <summary>Fake flavor repository for tests.</summary>
public class FakeFlavorRepository : IFlavorRepository
{
    public Dictionary<Guid, Flavor> Flavors { get; } = new();
    public Task<Flavor?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Flavors.TryGetValue(id, out var f) ? f : null);
    public Task<IReadOnlyList<Flavor>> ListActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Flavor>>(Flavors.Values.Where(f => f.Active).OrderBy(f => f.PricePerHour).ToList());
    public Task<IReadOnlyList<Flavor>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Flavor>>(Flavors.Values.OrderBy(f => f.PricePerHour).ToList());
    public Task AddAsync(Flavor flavor, CancellationToken ct) { Flavors[flavor.Id] = flavor; return Task.CompletedTask; }
}

/// <summary>Fake image repository for tests.</summary>
public class FakeImageRepository : IImageRepository
{
    public Dictionary<Guid, Image> Images { get; } = new();
    public Task<Image?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Images.TryGetValue(id, out var i) ? i : null);
    public Task<IReadOnlyList<Image>> ListActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Image>>(Images.Values.OrderBy(i => i.Name).ToList());
    public Task AddAsync(Image image, CancellationToken ct) { Images[image.Id] = image; return Task.CompletedTask; }
}

/// <summary>Fake provisioning backend — controllable success/fail for tests.</summary>
public class FakeProvisioningBackend : IProvisioningBackend
{
    public string Mode => "fake";
    public bool ProvisionShouldFail { get; set; }
    public bool StartShouldFail { get; set; }
    public bool StopShouldFail { get; set; }
    public bool TerminateShouldFail { get; set; }
    public int ProvisionCalls { get; private set; }
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int TerminateCalls { get; private set; }

    public string ProvisionedExternalIdFormat(Guid id) => $"fake-{id:N}";

    public Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct)
    {
        ProvisionCalls++;
        if (ProvisionShouldFail)
            return Task.FromResult(ProvisionResult.Fail("Backend declined"));
        // Production backends honor the IP the orchestrator allocated from
        // the /24 pool (10.42.0.0/24). Tests that don't pre-populate
        // NetworkService fall back to the legacy stub IP.
        var ip = request.IpAddress ?? "10.0.0.42";
        return Task.FromResult(ProvisionResult.Ok(
            ProvisionedExternalIdFormat(request.ResourceId), ip));
    }

    public Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct)
    {
        StartCalls++;
        if (StartShouldFail)
            return Task.FromResult(ProvisionResult.Fail("Start failed"));
        return Task.FromResult(ProvisionResult.Ok(externalId, "10.0.0.42"));
    }

    public Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct)
    {
        StopCalls++;
        if (StopShouldFail)
            return Task.FromResult(ProvisionResult.Fail("Stop failed"));
        return Task.FromResult(ProvisionResult.Ok(externalId, "10.0.0.42"));
    }

    public Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct)
    {
        TerminateCalls++;
        if (TerminateShouldFail)
            return Task.FromResult(ProvisionResult.Fail("Terminate failed"));
        return Task.FromResult(ProvisionResult.Ok(externalId, "10.0.0.42"));
    }

    public Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct) =>
        Task.FromResult(new ResourceUsage(42.5, 512, 100, 1024, 2048, DateTimeOffset.UtcNow));

    public Task<BackendHealth> GetHealthAsync(CancellationToken ct) =>
        Task.FromResult(new BackendHealth(Mode, true, null, DateTimeOffset.UtcNow));

    // ─── Interactive exec (test stub) ───────────────────────────────────
    // The fake session pipes InputStream → OutputStream so callers can
    // verify the WebSocket bridge plumbing without a real Docker exec.
    // Implementation: a queue-backed in-memory pipe. Bytes written to
    // InputStream are appended to the queue; OutputStream reads drain
    // it. Kill enqueues an EOF marker.

    public int ExecInteractiveCalls { get; private set; }
    public List<string> ExecInteractiveTargets { get; } = new();

    public Task<IShellSession> ExecInteractiveAsync(string externalId, CancellationToken ct)
    {
        ExecInteractiveCalls++;
        ExecInteractiveTargets.Add(externalId);
        return Task.FromResult<IShellSession>(new LoopbackShellSession());
    }

    private sealed class LoopbackShellSession : IShellSession
    {
        // ConcurrentQueue<byte[]> with a sentinel null = EOF. Tiny
        // helper to bridge the input stream and output stream without
        // taking on a real PTY.
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _queue = new();
        private readonly ManualResetEventSlim _dataAvailable = new(false);
        private readonly Stream _input;
        private readonly Stream _output;
        private bool _eof;
        private bool _disposed;

        public LoopbackShellSession()
        {
            _input = new WriteSide(this);
            _output = new ReadSide(this);
        }

        public Stream InputStream => _input;
        public Stream OutputStream => _output;
        public void Kill() => End();

        private void End()
        {
            _eof = true;
            _dataAvailable.Set();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            End();
            _input.Dispose();
            _output.Dispose();
            _dataAvailable.Dispose();
            await Task.CompletedTask;
        }

        private sealed class WriteSide : Stream
        {
            private readonly LoopbackShellSession _owner;
            public WriteSide(LoopbackShellSession o) => _owner = o;
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
            public override void Write(byte[] buffer, int offset, int count)
            {
                var copy = new byte[count];
                Buffer.BlockCopy(buffer, offset, copy, 0, count);
                _owner._queue.Enqueue(copy);
                _owner._dataAvailable.Set();
            }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
        }

        private sealed class ReadSide : Stream
        {
            private readonly LoopbackShellSession _owner;
            private byte[]? _carry;
            private int _carryPos;
            public ReadSide(LoopbackShellSession o) => _owner = o;
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
            public override int Read(byte[] buffer, int offset, int count)
                => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                while (_carry is null || _carryPos >= _carry.Length)
                {
                    if (_owner._eof && _owner._queue.IsEmpty) return 0;
                    if (!_owner._queue.TryDequeue(out _carry))
                    {
                        // Wait briefly for data or EOF.
                        _owner._dataAvailable.Wait(ct);
                        _owner._dataAvailable.Reset();
                        if (_owner._eof && _owner._queue.IsEmpty) return 0;
                        continue;
                    }
                    _carryPos = 0;
                }
                var n = Math.Min(count, _carry.Length - _carryPos);
                Buffer.BlockCopy(_carry, _carryPos, buffer, offset, n);
                _carryPos += n;
                return n;
            }
        }
    }
}
