# Plan 003: Enable real CSRF validation for cookie-authenticated mutations

> **Executor instructions**: Follow this plan step by step. Run every verification command and confirm the expected result before moving to the next step. If anything in the "STOP conditions" section occurs, stop and report — do not improvise. When done, update the status row for this plan in `plans/README.md` — unless a reviewer dispatched you and told you they maintain the index.
>
> **Drift check (run first)**: `git diff --stat 917e5f2..HEAD -- src/Pico.Api/Program.cs src/Pico.Api/Endpoints/AuthEndpoints.cs src/Pico.Api/Endpoints/ResourceEndpoints.cs src/Pico.Api/Endpoints/InvoiceEndpoints.cs frontend/src/lib/api.ts tests/Pico.Tests`
> If any in-scope file changed since this plan was written, compare the "Current state" excerpts against the live code before proceeding; on a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED — auth/session middleware changes can break login and all mutations if token issuance/validation is miswired.
- **Depends on**: none
- **Category**: security
- **Planned at**: commit `917e5f2`, 2026-07-01

## Why this matters

Pico uses HttpOnly cookie authentication and the frontend already attempts to fetch and send a CSRF token for unsafe requests. The backend registers antiforgery services and exposes a token endpoint, but no middleware/filter validates the token on JSON mutation endpoints. The current state gives a false sense of protection: reviewers see CSRF code, but state-changing endpoints still accept requests without a valid token.

## Current state

Relevant files:

- `src/Pico.Api/Program.cs` — registers cookie auth and antiforgery; maps CSRF token endpoint.
- `src/Pico.Api/Endpoints/AuthEndpoints.cs` — signup/login/logout mutations.
- `src/Pico.Api/Endpoints/ResourceEndpoints.cs` — provision/start/stop/delete mutations.
- `src/Pico.Api/Endpoints/InvoiceEndpoints.cs` — pay mutation.
- `frontend/src/lib/api.ts` — fetches and sends `X-CSRF-TOKEN` on unsafe methods.

Current excerpts:

```csharp
// src/Pico.Api/Program.cs:93-99
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "Pico.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
```

```csharp
// src/Pico.Api/Program.cs:133-138
app.MapGet("/api/auth/csrf-token", (Microsoft.AspNetCore.Antiforgery.IAntiforgery af, HttpContext ctx) =>
{
    var tokens = af.GetAndStoreTokens(ctx);
    return Results.Ok(new { token = tokens.RequestToken });
}).RequireAuthorization();
```

```ts
// frontend/src/lib/api.ts:80-82
if (unsafe && token) {
  requestHeaders["X-CSRF-TOKEN"] = token;
}
```

```ts
// frontend/src/lib/api.ts:98-103
const token = unsafe ? await fetchCsrfToken() : null;
let res = await makeRequest(token);

if (unsafe && res.status === 403 && token) {
  csrfToken = null;
  res = await makeRequest(await fetchCsrfToken());
}
```

Repo conventions to match:

- Minimal API endpoint groups are defined in `src/Pico.Api/Endpoints/*.cs` and registered from `Program.cs`.
- Error responses currently use simple `BadRequest`/`Forbid` objects; do not introduce a broad error-handling redesign here.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Build | `dotnet build --nologo` | exit 0 |
| Unit tests | `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` | exit 0 |
| Typecheck | `cd frontend && npx tsc --noEmit` | exit 0 |
| Frontend build | `cd frontend && npx next build` | exit 0 |
| Lint | `cd frontend && npx eslint .` | exit 0; current baseline has warnings |

## Scope

**In scope**:

- `src/Pico.Api/Program.cs`
- `src/Pico.Api/Endpoints/AuthEndpoints.cs`, `ResourceEndpoints.cs`, `InvoiceEndpoints.cs` only for applying validation metadata/filters if needed
- `frontend/src/lib/api.ts` only if token endpoint auth changes require client adjustment
- `tests/Pico.Tests` for CSRF regression tests

**Out of scope**:

- Do not replace cookie auth with bearer tokens.
- Do not add ASP.NET Identity or rate limiting here.
- Do not weaken CORS as a substitute for CSRF validation.

## Git workflow

- Branch suggestion: `advisor/003-enable-real-csrf-validation`
- Commit style: `fix: enforce csrf validation for mutations`
- Do not push or open a PR unless instructed.

## Steps

### Step 1: Add tests proving unsafe endpoints reject missing CSRF tokens

Add endpoint-level tests if possible. If `Microsoft.AspNetCore.Mvc.Testing` is not present, add it to `tests/Pico.Tests/Pico.Tests.csproj` as a test-only package and create a small `WebApplicationFactory<Program>` test fixture.

Cover at least:

- Authenticated `POST /api/resources` without `X-CSRF-TOKEN` is rejected.
- Authenticated `POST /api/invoices/{id}/pay` without `X-CSRF-TOKEN` is rejected, or use a simpler authenticated mutation if invoice setup is too heavy.
- `GET /api/catalog/flavors` and `GET /api/health` still work without a token.

**Verify**: `dotnet test --nologo --filter "FullyQualifiedName~Csrf"` → tests fail against current code because missing tokens are accepted or no validation is wired.

### Step 2: Make the token endpoint usable by the SPA

Decide whether login/signup should require CSRF. Recommended for this cookie-auth SPA: make `/api/auth/csrf-token` anonymous so the browser can obtain an antiforgery cookie before login, then require the token for all unsafe methods including login/signup/logout.

Implementation target:

```csharp
app.MapGet("/api/auth/csrf-token", (... ) => { ... })
   .AllowAnonymous();
```

If the team explicitly does not want CSRF on login/signup, document that in a code comment and apply validation to authenticated resource/invoice/logout mutations only.

**Verify**: `dotnet build --nologo` → exit 0.

### Step 3: Enforce antiforgery validation on unsafe JSON endpoints

ASP.NET antiforgery services alone do not protect these JSON Minimal API endpoints. Add an endpoint filter or middleware that validates unsafe methods with `IAntiforgery.ValidateRequestAsync(ctx)` for the mutation groups.

A maintainable shape is a small extension in API code, for example an endpoint filter factory applied to groups:

- `POST /api/auth/signup`, `POST /api/auth/login`, `POST /api/auth/logout` if Step 2 chose full coverage
- `POST /api/resources`, `POST /api/resources/{id}/start`, `POST /api/resources/{id}/stop`, `DELETE /api/resources/{id}`
- `POST /api/invoices/{id}/pay`

Return `Results.Forbid()` or a 400 problem response when validation fails. Keep the frontend header name `X-CSRF-TOKEN` aligned with `Program.cs`.

**Verify**: `dotnet test --nologo --filter "FullyQualifiedName~Csrf"` → CSRF tests pass.

### Step 4: Confirm frontend token retry still works

The current client fetches a token before unsafe methods and retries once on 403. If Step 2 made the token endpoint anonymous, login/signup should now include a token too. Do not store the token outside module memory.

**Verify**: `cd frontend && npx tsc --noEmit` → exit 0.

### Step 5: Run full verification

**Verify**:

- `dotnet build --nologo` → exit 0
- `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` → exit 0
- `cd frontend && npx tsc --noEmit` → exit 0
- `cd frontend && npx next build` → exit 0

## Test plan

- New endpoint-level CSRF tests under `tests/Pico.Tests`.
- Negative tests: authenticated unsafe request without header fails; bad token fails.
- Positive tests: token endpoint issues a token; same client sends unsafe request with header and succeeds or reaches normal business validation.
- Regression tests that safe GET endpoints do not require CSRF.

## Done criteria

- [ ] Backend validates CSRF tokens for chosen unsafe mutation endpoints.
- [ ] Token endpoint is reachable at the point the frontend needs it.
- [ ] Missing/bad token tests fail closed.
- [ ] Safe GET endpoints continue to work without tokens.
- [ ] `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` exits 0.
- [ ] `cd frontend && npx tsc --noEmit` exits 0.

## STOP conditions

Stop and report if:

- ASP.NET Core 10 antiforgery APIs differ from expected and validation cannot be added without broad auth redesign.
- Making the token endpoint anonymous conflicts with a documented security requirement.
- Tests require a real Postgres/Docker dependency; keep this plan's tests unit/in-memory or WebApplicationFactory-level.

## Maintenance notes

CSRF protection is easy to regress when adding new mutation endpoints. Add a small helper/extension so future endpoint groups opt into the same filter and reviewers can spot missing coverage. Treat CORS and SameSite as defense-in-depth, not a replacement for token validation.
