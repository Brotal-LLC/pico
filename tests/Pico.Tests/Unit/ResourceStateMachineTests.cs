using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Domain.StateMachines;

namespace Pico.Tests.Unit;

/// <summary>
/// State machine tests for ResourceStatus. Pure logic, no IO.
/// Verifies all valid transitions are allowed AND all invalid transitions are rejected.
/// </summary>
public class ResourceStateMachineTests
{
    [Theory]
    // From Created
    [InlineData(ResourceStatus.Created, ResourceStatus.Provisioning)]
    // From Provisioning
    [InlineData(ResourceStatus.Provisioning, ResourceStatus.Running)]
    [InlineData(ResourceStatus.Provisioning, ResourceStatus.Failed)]
    // From Running
    [InlineData(ResourceStatus.Running, ResourceStatus.Stopped)]
    [InlineData(ResourceStatus.Running, ResourceStatus.Terminated)]
    // From Stopped
    [InlineData(ResourceStatus.Stopped, ResourceStatus.Running)]
    [InlineData(ResourceStatus.Stopped, ResourceStatus.Terminated)]
    // From Failed
    [InlineData(ResourceStatus.Failed, ResourceStatus.Terminated)]
    public void CanTransition_ValidTransition_ReturnsTrue(ResourceStatus from, ResourceStatus to)
    {
        Assert.True(ResourceStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ResourceStatus.Created, ResourceStatus.Running)]
    [InlineData(ResourceStatus.Created, ResourceStatus.Stopped)]
    [InlineData(ResourceStatus.Created, ResourceStatus.Terminated)]
    [InlineData(ResourceStatus.Created, ResourceStatus.Failed)]
    [InlineData(ResourceStatus.Provisioning, ResourceStatus.Stopped)]
    [InlineData(ResourceStatus.Provisioning, ResourceStatus.Terminated)]
    [InlineData(ResourceStatus.Running, ResourceStatus.Failed)]
    [InlineData(ResourceStatus.Stopped, ResourceStatus.Failed)]
    [InlineData(ResourceStatus.Failed, ResourceStatus.Provisioning)]
    [InlineData(ResourceStatus.Failed, ResourceStatus.Running)]
    [InlineData(ResourceStatus.Terminated, ResourceStatus.Running)]
    [InlineData(ResourceStatus.Terminated, ResourceStatus.Stopped)]
    [InlineData(ResourceStatus.Terminated, ResourceStatus.Provisioning)]
    public void CanTransition_InvalidTransition_ReturnsFalse(ResourceStatus from, ResourceStatus to)
    {
        Assert.False(ResourceStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void CanTransition_ToSame_ReturnsFalse()
    {
        // Identity is never a valid "transition"
        Assert.False(ResourceStateMachine.CanTransition(ResourceStatus.Running, ResourceStatus.Running));
        Assert.False(ResourceStateMachine.CanTransition(ResourceStatus.Created, ResourceStatus.Created));
    }

    [Fact]
    public void EnsureTransition_Valid_DoesNotThrow()
    {
        ResourceStateMachine.EnsureTransition(ResourceStatus.Created, ResourceStatus.Provisioning);
        ResourceStateMachine.EnsureTransition(ResourceStatus.Running, ResourceStatus.Stopped);
        ResourceStateMachine.EnsureTransition(ResourceStatus.Stopped, ResourceStatus.Running);
        ResourceStateMachine.EnsureTransition(ResourceStatus.Failed, ResourceStatus.Terminated);
    }

    [Fact]
    public void EnsureTransition_Invalid_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            ResourceStateMachine.EnsureTransition(ResourceStatus.Terminated, ResourceStatus.Running));
    }

    [Fact]
    public void AllDefinedStatuses_HaveTransitionRules()
    {
        // Verify all 6 statuses are covered by at least one valid transition
        var statuses = Enum.GetValues<ResourceStatus>();
        var counts = new Dictionary<ResourceStatus, int>();
        foreach (var s in statuses) counts[s] = 0;

        foreach (var from in statuses)
        foreach (var to in statuses)
            if (ResourceStateMachine.CanTransition(from, to))
                counts[from]++;

        // Each non-terminal status should have at least one valid outgoing transition
        Assert.Equal(1, counts[ResourceStatus.Created]);
        Assert.Equal(2, counts[ResourceStatus.Provisioning]);
        Assert.Equal(2, counts[ResourceStatus.Running]);
        Assert.Equal(2, counts[ResourceStatus.Stopped]);
        Assert.Equal(1, counts[ResourceStatus.Failed]);
        // Terminated has 0 outgoing — it's terminal
        Assert.Equal(0, counts[ResourceStatus.Terminated]);
    }
}