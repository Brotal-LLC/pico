using System.Net;
using Pico.Application.Common;

namespace Pico.Application.Networking;

/// <summary>
/// Allocates IPv4 addresses for VM containers from a fixed /24 subnet
/// (10.42.0.0/24 by default). The .1 slot is reserved for the Docker
/// gateway and the .255 slot is the broadcast address — neither is
/// ever handed out.
///
/// State is held in memory only. On API startup the caller must invoke
/// <see cref="RepopulateAsync"/> with the resource repository so the
/// pool is rebuilt from non-Terminated resources' persisted IP
/// addresses; otherwise a restart would happily reassign IPs that
/// live containers are already using, causing IP collisions inside
/// the Docker bridge.
///
/// All public methods are thread-safe — concurrent Allocate calls
/// from the API's request pipeline must not return the same IP twice.
/// Released IPs are preferred over newly-scanned slots so churn
/// stays local to recently-released VMs.
/// </summary>
public class NetworkService
{
    private const string NetworkBase = "10.42.0."; // /24 → 254 usable slots
    private const int FirstUsable = 2;             // skip .1 (gateway)
    private const int LastUsable = 254;            // skip .255 (broadcast)

    // SortedSet of currently-free slot indices. Empty after RepopulateAsync
    // until the first AllocateAsync pops from it; we refill it lazily from
    // _next on demand.
    private readonly SortedSet<int> _free = new();
    // Slot → resourceId for diagnostics. Lookup is not on the hot path.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Guid> _owner = new();
    private int _next = FirstUsable;
    private readonly object _gate = new();

    public async Task<string> AllocateAsync(Guid resourceId, CancellationToken ct)
    {
        int slot;
        lock (_gate)
        {
            if (_free.Count == 0)
            {
                // Scan forward from _next, wrap once, fill _free with whatever's open.
                for (var i = 0; i < LastUsable - FirstUsable + 1; i++)
                {
                    var s = _next;
                    _next = _next >= LastUsable ? FirstUsable : _next + 1;
                    if (!_owner.ContainsKey(s)) _free.Add(s);
                    if (_free.Count > 0) break; // we just need one
                }
            }
            if (_free.Count == 0)
            {
                // Full pass done and nothing was free. Now actually exhaust.
                // Re-scan once more (in case all slots are claimed only above _next).
                for (var s = FirstUsable; s <= LastUsable; s++)
                    if (!_owner.ContainsKey(s)) _free.Add(s);
            }
            if (_free.Count == 0)
            {
                throw new NetworkExhaustedException(
                    $"No free IPs in {NetworkBase}0/{24} subnet. Pool size: {LastUsable - FirstUsable + 1}.");
            }
            slot = _free.Min;
            _free.Remove(slot);
            _next = slot >= LastUsable ? FirstUsable : slot + 1;
        }
        _owner[slot] = resourceId;
        await Task.CompletedTask; // API parity with RepopulateAsync
        return NetworkBase + slot;
    }

    public Task ReleaseAsync(string ip, CancellationToken ct)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return Task.CompletedTask;
        var bytes = addr.GetAddressBytes();
        if (bytes.Length != 4) return Task.CompletedTask;
        // only care about /24 within our base
        if (bytes[0] != 10 || bytes[1] != 42 || bytes[2] != 0) return Task.CompletedTask;
        var slot = bytes[3];
        if (slot < FirstUsable || slot > LastUsable) return Task.CompletedTask;
        lock (_gate)
        {
            if (_owner.TryRemove(slot, out _))
                _free.Add(slot);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reclaim IPs from non-Terminated resources so a fresh API process
    /// doesn't reassign them. Idempotent — safe to call multiple times.
    /// </summary>
    public async Task RepopulateAsync(IResourceRepository repo, CancellationToken ct)
    {
        var all = await repo.ListAllAsync(ct);
        lock (_gate)
        {
            _free.Clear();
            _owner.Clear();
            foreach (var r in all)
            {
                if (r.IsTerminated()) continue;
                if (string.IsNullOrWhiteSpace(r.IpAddress)) continue;
                if (!IPAddress.TryParse(r.IpAddress, out var addr)) continue;
                var bytes = addr.GetAddressBytes();
                if (bytes.Length != 4 || bytes[0] != 10 || bytes[1] != 42 || bytes[2] != 0) continue;
                var slot = bytes[3];
                if (slot < FirstUsable || slot > LastUsable) continue;
                _owner[slot] = r.Id;
            }
            // _free starts empty — first AllocateAsync will scan and fill it.
            // Start the scan from the lowest-allocated slot so the next free
            // slot is right after the densest cluster.
            var lowest = _owner.Keys.DefaultIfEmpty(FirstUsable - 1).Min();
            _next = lowest >= LastUsable ? FirstUsable : Math.Max(FirstUsable, lowest + 1);
            if (_next > LastUsable) _next = FirstUsable;
        }
    }
}