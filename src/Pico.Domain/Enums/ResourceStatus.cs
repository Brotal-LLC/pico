namespace Pico.Domain.Enums;

/// <summary>
/// Resource lifecycle status. Transitions enforced by ResourceStateMachine.
/// </summary>
public enum ResourceStatus
{
    Created = 0,
    Provisioning = 1,
    Running = 2,
    Stopped = 3,
    Terminated = 4,
    Failed = 5,
}