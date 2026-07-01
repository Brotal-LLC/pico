# Plan 004: Clear high-severity dependency advisories

> **Executor instructions**: Follow this plan step by step. Run every verification command and confirm the expected result before moving to the next step. If anything in the "STOP conditions" section occurs, stop and report — do not improvise. When done, update the status row for this plan in `plans/README.md` — unless a reviewer dispatched you and told you they maintain the index.
>
> **Drift check (run first)**: `git diff --stat 917e5f2..HEAD -- Directory.Build.props src/Pico.Api/Pico.Api.csproj src/Pico.Infrastructure/Pico.Infrastructure.csproj tests/Pico.Tests/Pico.Tests.csproj frontend/package.json frontend/package-lock.json`
> If any in-scope file changed since this plan was written, compare the "Current state" excerpts against the live code before proceeding; on a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED — package upgrades can change OpenAPI generation, EF design-time behavior, or Docker client behavior.
- **Depends on**: none
- **Category**: migration
- **Planned at**: commit `917e5f2`, 2026-07-01

## Why this matters

The repo's own `dotnet list package --vulnerable --include-transitive` reports high-severity vulnerable transitive packages in runtime projects. For a Lead Full-Stack take-home, visible high-severity advisories are a review distraction and a real maintenance issue. The fix should either upgrade the dependency chain or add a narrow, documented suppression only when the affected package is not reachable.

## Current state

Relevant files:

- `Directory.Build.props` — NuGet audit is enabled in direct mode only.
- `src/Pico.Api/Pico.Api.csproj` — OpenAPI/Swagger dependencies bring `Microsoft.OpenApi 2.0.0`.
- `src/Pico.Infrastructure/Pico.Infrastructure.csproj` — EF design-time package brings design/build transitive packages including `System.Security.Cryptography.Xml 9.0.0`.
- `tests/Pico.Tests/Pico.Tests.csproj` — references Infrastructure and inherits vulnerable transitives.
- `frontend/package.json` / `package-lock.json` — npm audit currently reports moderate PostCSS advisories through Next.

Current excerpts:

```xml
<!-- Directory.Build.props:10-11 -->
<NuGetAudit>true</NuGetAudit>
<NuGetAuditMode>direct</NuGetAuditMode>
```

```xml
<!-- src/Pico.Api/Pico.Api.csproj:14-18 -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="7.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
<PackageReference Include="FluentValidation.AspNetCore" Version="11.3.0" />
```

```xml
<!-- src/Pico.Infrastructure/Pico.Infrastructure.csproj:15-24 -->
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0" />
...
<PackageReference Include="Docker.DotNet" Version="3.125.15" />
```

Advisor verification output:

```text
Project `Pico.Api` high advisory:
> Microsoft.OpenApi 2.0.0  GHSA-v5pm-xwqc-g5wc

Project `Pico.Infrastructure` / `Pico.Tests` high advisories:
> System.Security.Cryptography.Xml 9.0.0  GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf
```

Repo conventions to match:

- Keep Domain dependency-free.
- Prefer direct package references over hidden transitive surprises when they are needed to force patched versions.
- Do not disable audit globally to hide advisories.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| NuGet audit | `dotnet list package --vulnerable --include-transitive` | no high-severity advisories in runtime projects, or only documented non-runtime design-time exceptions |
| npm audit | `cd frontend && npm audit --audit-level=high --omit=dev` | exit 0 |
| Build | `dotnet build --nologo` | exit 0 |
| Unit tests | `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` | exit 0 |
| Frontend typecheck/build | `cd frontend && npx tsc --noEmit && npx next build` | exit 0 |

## Scope

**In scope**:

- `Directory.Build.props`
- `src/Pico.Api/Pico.Api.csproj`
- `src/Pico.Infrastructure/Pico.Infrastructure.csproj`
- `tests/Pico.Tests/Pico.Tests.csproj`
- `frontend/package.json`
- `frontend/package-lock.json`

**Out of scope**:

- Do not change source code behavior unless a package upgrade requires a small compile fix.
- Do not remove Swagger/OpenAPI entirely unless no patched version is available and reviewer approval is obtained.
- Do not run `npm audit fix --force` blindly; advisor output showed it proposed a nonsensical downgrade to Next 9.

## Git workflow

- Branch suggestion: `advisor/004-clear-high-severity-dependency-advisories`
- Commit style: `fix: clear high severity dependency advisories`
- Do not push or open a PR unless instructed.

## Steps

### Step 1: Identify the package chains and patched versions

Run:

```bash
dotnet list src/Pico.Api/Pico.Api.csproj package --include-transitive
dotnet list src/Pico.Infrastructure/Pico.Infrastructure.csproj package --include-transitive
dotnet list package --vulnerable --include-transitive
```

Find which top-level packages introduce `Microsoft.OpenApi 2.0.0` and `System.Security.Cryptography.Xml 9.0.0`. Check NuGet for patched versions compatible with .NET 10.

**Verify**: record the chain and chosen target versions in your notes before editing.

### Step 2: Upgrade or pin patched NuGet packages narrowly

Prefer upgrading top-level packages such as `Swashbuckle.AspNetCore` / OpenAPI-related packages if patched versions exist. If `System.Security.Cryptography.Xml` is only transitive through design-time tooling and a patched package exists, add a direct `PackageReference` to the patched version in the projects that need it.

Do not change `Pico.Domain/Pico.Domain.csproj`; Domain must remain dependency-free.

**Verify**: `dotnet restore` → exit 0.

### Step 3: Re-run NuGet audit and build/tests

Run:

```bash
dotnet list package --vulnerable --include-transitive
dotnet build --nologo
dotnet test --nologo --filter "FullyQualifiedName!~Integration"
```

**Verify**: no high-severity runtime advisories remain; build and unit tests pass.

### Step 4: Check frontend audit without forcing downgrades

Run:

```bash
cd frontend
npm audit --audit-level=high --omit=dev
```

Advisor saw only moderate advisories through `next`/`postcss`, so this command should exit 0. If a high advisory appears, upgrade within the existing Next 16/React 19 stack using normal `npm install <pkg>@<patched>` or package updates; do not accept `npm audit fix --force` if it downgrades major frameworks.

**Verify**: `npm audit --audit-level=high --omit=dev` exits 0.

### Step 5: Consider audit mode tightening

After advisories are clear, consider changing `Directory.Build.props` from `direct` to `all` so future transitive high advisories are visible. If enabling `all` causes noisy low/moderate warnings, keep it at `direct` but add a comment explaining the choice and ensure the explicit `dotnet list package --vulnerable --include-transitive` command remains documented in `plans/README.md` or project docs.

**Verify**: `dotnet build --nologo` still exits 0.

## Test plan

- Dependency changes are verified by restore/build/test rather than new unit tests.
- If OpenAPI/Swagger package upgrades are involved, run the API in development and manually confirm `/swagger` still renders if practical.
- Keep unit tests green: `dotnet test --nologo --filter "FullyQualifiedName!~Integration"`.

## Done criteria

- [ ] `dotnet list package --vulnerable --include-transitive` shows no high-severity advisories affecting runtime projects, or any remaining design-time-only advisory is narrowly documented with rationale.
- [ ] No global audit disable/suppression was added.
- [ ] `dotnet build --nologo` exits 0.
- [ ] `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` exits 0.
- [ ] `cd frontend && npm audit --audit-level=high --omit=dev` exits 0.
- [ ] `cd frontend && npx tsc --noEmit && npx next build` exits 0.

## STOP conditions

Stop and report if:

- The only available fix requires downgrading Next, .NET, EF Core, or another core framework major version.
- A patched NuGet package is incompatible with .NET 10 packages in this repo.
- Swagger/OpenAPI breaks in a way that requires redesigning API documentation.

## Maintenance notes

Keep dependency audit output in CI once CI exists. Avoid suppressing NU190x warnings broadly; narrow suppressions should include advisory IDs, why the vulnerable code is unreachable, and a date to revisit.
