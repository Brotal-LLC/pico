# Plan 001: Harden resource lifecycle actions before backend side effects

> **Executor instructions**: Follow this plan step by step. Run every verification command and confirm the expected result before moving to the next step. If anything in the "STOP conditions" section occurs, stop and report — do not improvise. When done, update the status row for this plan in `plans/README.md` — unless a reviewer dispatched you and told you they maintain the index.
>
> **Drift check (run first)**: `git diff --stat 917e5f2..HEAD -- src/Pico.Application/Resources/ResourceService.cs src/Pico.Api/Endpoints/ResourceEndpoints.cs src/Pico.Domain/StateMachines/ResourceStateMachine.cs tests/Pico.Tests/Unit/ResourceServiceTests.cs`
> If any in-scope file changed since this plan was written, compare the "Current state" excerpts against the live code before proceeding; on a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED — lifecycle behavior is central to the product and touches external backends.
- **Depends on**: none
- **Category**: bug
- **Planned at**: commit `917e5f2`, 2026-07-01

## Why this matters

The API can invoke a backend operation before proving that the requested state transition is legal. A direct API call can `start` an already-running VM, `stop` an already-stopped VM, or `terminate` a `Provisioning` resource; the backend may perform the side effect first, then `Resource.TransitionTo(...)` throws a domain exception and the endpoint returns a 500. In OpenStack/Docker modes this can leave Pico's database out of sync with the actual infrastructure. Fixing this makes lifecycle endpoints predictable and safer.

## Current state

Relevant files:

- `src/Pico.Application/Resources/ResourceService.cs` — lifecycle service; calls backend before transition validation in start/stop/terminate.
- `src/Pico.Domain/StateMachines/ResourceStateMachine.cs` — authoritative allowed transitions.
- `src/Pico.Api/Endpoints/ResourceEndpoints.cs` — maps lifecycle failures to HTTP responses.
- `tests/Pico.Tests/Unit/ResourceServiceTests.cs` — current unit coverage for happy paths and ownership only.

Current excerpts:

```csharp
// src/Pico.Domain/StateMachines/ResourceStateMachine.cs:20-25
[ResourceStatus.Created]      = new() { ResourceStatus.Provisioning },
[ResourceStatus.Provisioning] = new() { ResourceStatus.Running, ResourceStatus.Failed },
[ResourceStatus.Running]      = new() { ResourceStatus.Stopped, ResourceStatus.Terminated },
[ResourceStatus.Stopped]      = new() { ResourceStatus.Running, ResourceStatus.Terminated },
[ResourceStatus.Failed]       = new() { ResourceStatus.Terminated },
[ResourceStatus.Terminated]   = new(),
```

```csharp
// src/Pico.Application/Resources/ResourceService.cs:143-150
// Call backend first, then update state
var backendResult = await _backend.StartAsync(resource.ExternalId, ct);
if (!backendResult.Success)
    return Result<ResourceSummaryDto>.Failure($"Backend start failed: {backendResult.Error}");

var prev = resource.Status;
resource.TransitionTo(ResourceStatus.Running, "User started");
```

```csharp
// src/Pico.Application/Resources/ResourceService.cs:189-199
// Call backend first (if external id exists)
if (resource.ExternalId is not null)
{
    var backendResult = await _backend.TerminateAsync(resource.ExternalId, ct);
    if (!backendResult.Success)
        return Result<ResourceSummaryDto>.Failure($"Backend terminate failed: {backendResult.Error}");
}

var prev = resource.Status;
resource.TransitionTo(ResourceStatus.Terminated, "User terminated");
```

Repo conventions to match:

- Application services return `Result<T>` rather than throwing for expected business failures; see `ResourceService.StartAsync` returning `Failure("Forbidden")` at `src/Pico.Application/Resources/ResourceService.cs:137-146`.
- Domain transition legality belongs in `ResourceStateMachine`; do not duplicate a separate transition table in API code.
- Tests use xUnit with in-memory fakes in `tests/Pico.Tests/Helpers/FakeRepositories.cs`.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Build | `dotnet build --nologo` | exit 0; advisor saw 0 errors, 0 warnings |
| Unit tests | `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` | exit 0; advisor saw 95 passed |
| Frontend typecheck | `cd frontend && npx tsc --noEmit` | exit 0, no errors |
| Frontend build | `cd frontend && npx next build` | exit 0 |
| Frontend lint | `cd frontend && npx eslint .` | exit 0; current baseline has 4 warnings |

## Scope

**In scope**:

- `src/Pico.Application/Resources/ResourceService.cs`
- `src/Pico.Api/Endpoints/ResourceEndpoints.cs` only if needed to map new failure messages cleanly
- `tests/Pico.Tests/Unit/ResourceServiceTests.cs`
- `tests/Pico.Tests/Helpers/FakeRepositories.cs` only if the fake backend needs call counters

**Out of scope**:

- Do not change the state-machine graph unless the product decision is explicitly to allow cancelling `Provisioning` resources.
- Do not change Docker/OpenStack backend implementations in this plan.
- Do not introduce a background provisioning worker.

## Git workflow

- Branch suggestion: `advisor/001-harden-resource-lifecycle-actions`
- Commit style: follow existing log style such as `fix: comprehensive hardening pass — all critical/high review findings`.
- Do not push or open a PR unless the operator instructed it.

## Steps

### Step 1: Add regression tests for invalid lifecycle requests

In `tests/Pico.Tests/Unit/ResourceServiceTests.cs`, add tests that prove invalid requests fail without throwing:

- `StartAsync_RunningResource_ReturnsFailureWithoutBackendCall`
- `StopAsync_StoppedResource_ReturnsFailureWithoutBackendCall`
- `TerminateAsync_ProvisioningResource_ReturnsFailureWithoutBackendCall` if the current state machine remains unchanged.

If `FakeProvisioningBackend` lacks call counters, add simple integer counters (`StartCalls`, `StopCalls`, `TerminateCalls`) in `tests/Pico.Tests/Helpers/FakeRepositories.cs` and increment them at the start of each backend method.

**Verify**: `dotnet test --nologo --filter "FullyQualifiedName~ResourceServiceTests"` → the new tests fail against current code because the backend is called and/or a `DomainException` escapes.

### Step 2: Validate transition legality before backend calls

In `ResourceService.StartAsync`, `StopAsync`, and `TerminateAsync`, check the domain transition before calling the backend. Use the existing `ResourceStateMachine.CanTransition(resource.Status, targetStatus)` or a small private helper in `ResourceService` that returns a `Result<ResourceSummaryDto>.Failure(...)` with a clear message like `Invalid transition: Running -> Running`.

Target behavior:

- `StartAsync` only calls backend if `Stopped -> Running` is allowed.
- `StopAsync` only calls backend if `Running -> Stopped` is allowed.
- `TerminateAsync` only calls backend if the current state may transition to `Terminated` (`Running`, `Stopped`, or `Failed` today).
- Expected business failures return `Result.Failure(...)`; they do not throw.

**Verify**: `dotnet test --nologo --filter "FullyQualifiedName~ResourceServiceTests"` → all ResourceService tests, including the new invalid-transition tests, pass.

### Step 3: Keep API responses stable and non-500

Review `src/Pico.Api/Endpoints/ResourceEndpoints.cs` mappings. Invalid transitions should become `400 Bad Request` with a useful `detail`, not unhandled exceptions. The existing pattern already maps non-`Forbidden` failures to `Results.BadRequest(new { detail = result.ErrorMessage })`; only change this file if Step 2 introduces new result shapes.

**Verify**: `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` → all 95+ unit tests pass.

### Step 4: Run full verification gates

Run the repo's full cheap verification set.

**Verify**:

- `dotnet build --nologo` → exit 0
- `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` → exit 0
- `cd frontend && npx tsc --noEmit` → exit 0
- `cd frontend && npx eslint .` → exit 0 (warnings are acceptable only if they match the pre-existing unused-variable baseline)

## Test plan

- New unit tests in `tests/Pico.Tests/Unit/ResourceServiceTests.cs` for invalid lifecycle actions.
- Use the existing `StartAsync_StoppedResource_TransitionsToRunning` and `StartAsync_OtherUsersResource_ReturnsForbidden` structure as the pattern.
- Assert both the returned failure and that the relevant fake backend call count is zero.

## Done criteria

- [ ] Invalid start/stop/terminate requests return `Result.Failure(...)` instead of throwing.
- [ ] Backend call counters prove invalid requests do not touch Docker/OpenStack/mock backends.
- [ ] Existing happy-path lifecycle tests still pass.
- [ ] `dotnet build --nologo` exits 0.
- [ ] `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` exits 0.
- [ ] No files outside the in-scope list are modified except `plans/README.md` status update.

## STOP conditions

Stop and report if:

- Product owners want `Provisioning -> Terminated` cancellation; that is a state-machine/product change beyond this safety fix.
- Fixing invalid transitions requires changing provisioning backend semantics.
- Current code excerpts do not match the live files.

## Maintenance notes

Reviewers should scrutinize the ordering: validation must happen before backend side effects, and persisted state changes must still happen only after backend success. Future async/background provisioning work should reuse these precondition tests so API calls remain safe while resources are in intermediate states.
