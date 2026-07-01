# Plan 002: Restore the current-user session contract

> **Executor instructions**: Follow this plan step by step. Run every verification command and confirm the expected result before moving to the next step. If anything in the "STOP conditions" section occurs, stop and report — do not improvise. When done, update the status row for this plan in `plans/README.md` — unless a reviewer dispatched you and told you they maintain the index.
>
> **Drift check (run first)**: `git diff --stat 917e5f2..HEAD -- src/Pico.Api/Endpoints/AuthEndpoints.cs frontend/src/lib/api.ts frontend/src/components/AuthProvider.tsx frontend/src/components/Sidebar.tsx tests/Pico.Tests`
> If any in-scope file changed since this plan was written, compare the "Current state" excerpts against the live code before proceeding; on a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW — this is an API response-shape fix for an endpoint whose TypeScript contract already expects the richer shape.
- **Depends on**: none
- **Category**: bug
- **Planned at**: commit `917e5f2`, 2026-07-01

## Why this matters

After a browser refresh, the frontend calls `/api/auth/me` and treats the response as an `AuthUser` with `id`, `email`, `name`, and `role`. The API currently returns only `{ id }`. That means the dashboard can consider the user authenticated while the sidebar loses the name/email and admin users lose the admin navigation entry until they log in again. For a reviewer demo, this makes session persistence look broken.

## Current state

Relevant files:

- `src/Pico.Api/Endpoints/AuthEndpoints.cs` — `/api/auth/me` response is too small.
- `frontend/src/lib/api.ts` — TypeScript `AuthUser` contract expects full user shape.
- `frontend/src/components/AuthProvider.tsx` — stores `auth.me()` response directly as `user`.
- `frontend/src/components/Sidebar.tsx` — renders `user.name`, `user.email`, and gates admin nav on `user.role`.

Current excerpts:

```csharp
// src/Pico.Api/Endpoints/AuthEndpoints.cs:69-74
group.MapGet("/me", (HttpContext ctx) =>
{
    var userId = GetCurrentUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    return Results.Ok(new { id = userId.Value });
}).RequireAuthorization();
```

```ts
// frontend/src/lib/api.ts:143-148
export interface AuthUser {
  id: string;
  email: string;
  name: string;
  role: "Customer" | "Admin";
}
```

```tsx
// frontend/src/components/Sidebar.tsx:38-40
if (user?.role === "Admin") {
  nav.push({ href: "/admin", label: "Admin", icon: Shield });
}
```

```tsx
// frontend/src/components/Sidebar.tsx:137-140
<p className="text-sm font-medium truncate">{user?.name}</p>
<p className="text-xs text-muted-foreground truncate">{user?.email}</p>
<span className="font-mono text-xs">{user?.role}</span>
```

Repo conventions to match:

- `AuthEndpoints.SignInAsync` already stores `ClaimTypes.Email`, `ClaimTypes.Name`, and `ClaimTypes.Role` in the cookie claims at `src/Pico.Api/Endpoints/AuthEndpoints.cs:81-87`.
- Existing DTO `AuthUserDto(Guid Id, string Email, string Name, UserRole Role)` is already used for login/signup responses.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Build | `dotnet build --nologo` | exit 0; advisor saw 0 errors, 0 warnings |
| Unit tests | `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` | exit 0; advisor saw 95 passed |
| Typecheck | `cd frontend && npx tsc --noEmit` | exit 0, no errors |
| Frontend build | `cd frontend && npx next build` | exit 0 |
| Lint | `cd frontend && npx eslint .` | exit 0; current baseline has 4 warnings |

## Scope

**In scope**:

- `src/Pico.Api/Endpoints/AuthEndpoints.cs`
- `tests/Pico.Tests` only for a new focused test if adding endpoint-level tests is already supported or can be added narrowly
- `frontend/src/lib/api.ts` only if you need to adjust/strengthen the type contract

**Out of scope**:

- Do not change login/signup behavior.
- Do not add a new auth system or ASP.NET Identity in this plan.
- Do not redesign admin authorization.

## Git workflow

- Branch suggestion: `advisor/002-restore-current-user-session-contract`
- Commit style: follow existing `fix:` / `feat(...):` style.
- Do not push or open a PR unless instructed.

## Steps

### Step 1: Return the full user DTO from `/api/auth/me`

Update `group.MapGet("/me", ...)` in `AuthEndpoints.cs` to build an `AuthUserDto` from cookie claims. Use the current claims written by `SignInAsync`:

- `ClaimTypes.NameIdentifier` → `Guid Id`
- `ClaimTypes.Email` → `Email`
- `ClaimTypes.Name` → `Name`
- `ClaimTypes.Role` → parse to `UserRole`

If any required claim is missing or the role cannot be parsed, return `Results.Unauthorized()` rather than returning a partial object.

**Verify**: `dotnet build --nologo` → exit 0.

### Step 2: Add a regression test for the contract

If the test project already has endpoint-test infrastructure, add a test that signs in or constructs an authenticated request and asserts `/api/auth/me` returns `id`, `email`, `name`, and `role`. If endpoint-test infrastructure is absent, add a small unit-level test around any helper you introduce for converting claims to `AuthUserDto`.

**Verify**: `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` → all tests pass, including the new contract test.

### Step 3: Confirm frontend contract remains aligned

No frontend change should be necessary if the API returns the existing `AuthUser` shape. Run TypeScript to ensure no drift.

**Verify**: `cd frontend && npx tsc --noEmit` → exit 0, no errors.

### Step 4: Run full cheap verification

**Verify**:

- `dotnet build --nologo` → exit 0
- `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` → exit 0
- `cd frontend && npx tsc --noEmit` → exit 0
- `cd frontend && npx next build` → exit 0

## Test plan

- New contract test for `/api/auth/me` or a helper used by it.
- Manual reviewer check after running the app: log in as `admin@pico.local`, refresh `/dashboard`, and confirm sidebar still shows Admin nav plus name/email/role.

## Done criteria

- [ ] `/api/auth/me` returns the same `AuthUserDto` shape as login/signup.
- [ ] Missing/malformed auth claims return 401, not partial user data.
- [ ] Sidebar can keep showing `name`, `email`, and `role` after refresh.
- [ ] `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` exits 0.
- [ ] `cd frontend && npx tsc --noEmit` exits 0.
- [ ] No out-of-scope auth redesign was introduced.

## STOP conditions

Stop and report if:

- The live code no longer stores email/name/role claims in the auth cookie.
- Implementing this requires database lookups on every `/me` request; that is acceptable only if explicitly chosen, and should be discussed first.
- Current excerpts do not match the live files.

## Maintenance notes

Keep API DTOs and frontend interfaces in sync. If future auth work switches from claims-only sessions to database-backed sessions, preserve this `/me` contract or update `frontend/src/lib/api.ts` and all consumers in the same change.
