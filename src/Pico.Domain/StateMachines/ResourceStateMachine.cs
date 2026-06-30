namespace Pico.Domain.StateMachines;

using Pico.Domain.Enums;

/// <summary>
/// Pure logic: validates whether a transition between two ResourceStatus values is allowed.
/// Lives in Domain, depends on nothing else. Called by Resource.SetStatus() and Application services.
/// </summary>
public static class ResourceStateMachine
{
    // Allowed forward transitions in the state graph:
    //   Created    → Provisioning
    //   Provisioning → Running | Failed
    //   Running    → Stopped | Terminated
    //   Stopped    → Running | Terminated
    //   Failed     → Terminated
    //   Terminated → (terminal; no outgoing)
    private static readonly Dictionary<ResourceStatus, HashSet<ResourceStatus>> _allowed = new()
    {
        [ResourceStatus.Created]      = new() { ResourceStatus.Provisioning },
        [ResourceStatus.Provisioning] = new() { ResourceStatus.Running, ResourceStatus.Failed },
        [ResourceStatus.Running]      = new() { ResourceStatus.Stopped, ResourceStatus.Terminated },
        [ResourceStatus.Stopped]      = new() { ResourceStatus.Running, ResourceStatus.Terminated },
        [ResourceStatus.Failed]       = new() { ResourceStatus.Terminated },
        [ResourceStatus.Terminated]   = new(),
    };

    /// <summary>Returns true if `from → to` is permitted. Same-state is never a transition.</summary>
    public static bool CanTransition(ResourceStatus from, ResourceStatus to)
    {
        if (from == to) return false;
        return _allowed.TryGetValue(from, out var set) && set.Contains(to);
    }

    /// <summary>Throws DomainException if transition is not allowed.</summary>
    public static void EnsureTransition(ResourceStatus from, ResourceStatus to)
    {
        if (!CanTransition(from, to))
            throw new DomainException(
                $"Invalid status transition: {from} → {to}");
    }
}