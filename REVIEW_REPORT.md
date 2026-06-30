# PICO Full Codebase Review Report

Date: 2026-06-30  
Reviewer: Pixu  
Scope: entire repo at `/home/shakib/repos/pico` (`main`, HEAD `665b50c`)

## Executive verdict

PICO has a solid-looking skeleton: clean project split, a real domain model, a reasonable state machine, 91 fast unit tests, and a polished-ish Next.js surface. But the repository is **not submission-ready** yet.

The largest problems are not aesthetic. They are reviewer-killers:

1. **`docker compose up --build` quickstart is broken.** Docker builds fail, compose requires external networks, no localhost ports are exposed, and frontend defaults point to a cloud API URL.
2. **Demo login flow is broken.** Seed config does not run, and seeded password hashes do not match login verification.
3. **Provisioning lifecycle is broken.** Mock provisioning leaves resources stuck in `Provisioning`; UI disables Start in that state; tests manually force state transitions to hide the gap.
4. **Resource read/usage endpoints have authorization bugs.** `GET /api/resources/{id}` and `GET /api/resources/{id}/usage` do not enforce ownership.
5. **OpenStack/Docker backends are substantially stubs while docs present them as real.** OpenStack uses hardcoded flavor/image refs and no Nova base URL; Docker ignores flavor/image.
6. **Testing claims are inflated.** Integration tests fail, frontend tests do not exist, E2E tests do not exist, lint is broken, and pre-commit is not versioned.
7. **Security posture is demo-grade.** SHA256 password hashing, no CSRF defense despite cookie auth, no rate limiting, vulnerable dependencies suppressed globally.

This can still be turned into an impressive take-home, but it needs a focused hardening pass before publication/submission.

---

## Verification commands run

### Passing

```bash
cd ~/repos/pico
dotnet build --nologo
# ok dotnet build: 6 projects, 0 errors, 0 warnings

cd ~/repos/pico
dotnet test --nologo --filter "FullyQualifiedName!~Integration"
# Passed: 91, Failed: 0

cd ~/repos/pico/frontend
npx tsc --noEmit
# TypeScript: no errors

cd ~/repos/pico/frontend
npx next build
# Builds 12 app routes successfully
```

### Failing / broken

```bash
cd ~/repos/pico
dotnet test tests/Pico.Tests/Pico.Tests.csproj --filter "FullyQualifiedName~Integration" --nologo
# Failed: 5/5 integration tests
# Docker API responded with BadRequest: invalid port specification: "0/tcp"
```

```bash
cd ~/repos/pico
docker build -f backend/Dockerfile.dev .
# Fails at groupadd --gid 1000 pixu (exit code 4)
```

```bash
cd ~/repos/pico
docker build -f backend/Dockerfile.prod .
# Fails: COPY Pico.sln ./ — /Pico.sln not found; repo has Pico.slnx
```

```bash
cd ~/repos/pico/frontend
docker build -f Dockerfile.prod .
# Fails while creating pixu UID/GID 1000 in runtime image
```

```bash
cd ~/repos/pico/frontend
npm run lint
# Fails: Next 16 treats `next lint` as invalid project directory `lint`
```

```bash
cd ~/repos/pico/frontend
npm test -- --run
# Fails: No test files found

npm run e2e
# Fails: No Playwright tests found
```

```bash
cd ~/repos/pico
dotnet list package --vulnerable --include-transitive
# High severity advisories:
# - Microsoft.OpenApi 2.0.0
# - System.Security.Cryptography.Xml 9.0.0
```

```bash
cd ~/repos/pico/frontend
npm audit --omit=dev
# 2 moderate vulnerabilities through Next's postcss dependency
```

---

## Critical findings

### CRITICAL-01 — Reviewer quickstart is broken

**Files:**
- `README.md:9-23`
- `compose.yaml:40-123`
- `backend/Dockerfile.dev:5-18`
- `backend/Dockerfile.prod:11-12`
- `frontend/Dockerfile.prod:18-25`

**Evidence:**
- README says: `cp .env.example .env && docker compose up --build`, then open `localhost:3000` and `localhost:8080`.
- Compose exposes **no `ports:`** for frontend or API.
- Compose requires external networks `ingress` and `database`; a reviewer laptop will not have them.
- `frontend` default `NEXT_PUBLIC_API_URL` is `https://pico-api.aamar.cloud`, not local API.
- API dev service mounts `./backend:/workspace`; actual .NET solution lives in repo root `src/`, so hot-reload container will not see project files.
- Backend dev Docker build fails at `groupadd --gid 1000 pixu`.
- Backend prod Dockerfile references `Pico.sln`, but the repo has `Pico.slnx`.
- Frontend prod Docker build fails creating UID/GID 1000 user.

**Impact:** The first command in the README likely fails for a reviewer. Even if compose starts after manual edits, localhost URLs do not work as documented.

**Fix:**
- Split compose into `compose.local.yaml` and `compose.deploy.yaml`, or make external networks optional via profiles.
- Add localhost ports:
  - frontend `3000:3000`
  - API `8080:8080`
- Mount repo root into API dev container: `.:/workspace`.
- Fix Docker non-root creation to handle existing UID/GID 1000.
- Use `Pico.slnx` or restore/publish project directly in prod Dockerfile.
- Set local frontend default `NEXT_PUBLIC_API_URL=http://localhost:8080`.

---

### CRITICAL-02 — Demo credentials cannot work

**Files:**
- `README.md:25-30`
- `compose.yaml:52-53`
- `.env.example:15-17`
- `src/Pico.Infrastructure/Seed/DatabaseInitializer.cs:33-35`
- `src/Pico.Infrastructure/Seed/DataSeeder.cs:45-61`
- `src/Pico.Api/Endpoints/AuthEndpoints.cs:32-55`

**Evidence:**
- Compose/env set `DemoData__Enabled=true`.
- Code reads `Database:SeedDemoData`, not `DemoData:Enabled`, so seeding does not run.
- DataSeeder writes hashes like `argon2id$demo$pico-demo-password` and `argon2id$admin$pico-admin-password`.
- Login computes `argon2id$dev$base64(SHA256(password))` and compares exact string.

**Impact:** README demo credentials are dead. `demo@pico.local / pico-demo-password` and admin login will fail even if the DB seeds manually.

**Fix:**
- Align config key: either code reads `DemoData:Enabled` or compose sets `Database__SeedDemoData=true`.
- Centralize password hashing in a `IPasswordHasher` service.
- Seed users with the same hasher used by login.
- Add an API/integration test: seed DB → login demo/admin succeeds.

---

### CRITICAL-03 — Provisioning flow never reaches `Running`

**Files:**
- `src/Pico.Application/Resources/ResourceService.cs:65-95`
- `src/Pico.Infrastructure/Provisioning/MockProvisioningBackend.cs:17-25`
- `frontend/src/app/(dashboard)/resources/[id]/page.tsx:92-145`
- `tests/Pico.Tests/Integration/RepositoryIntegrationTests.cs:135-143`
- `tests/Pico.Tests/Unit/ResourceServiceTests.cs` (manual transition pattern)
- `README.md:39`

**Evidence:**
- `ProvisionAsync` creates `Created`, calls backend, then transitions to `Provisioning` and returns.
- No background service transitions `Provisioning -> Running`.
- Mock backend waits 2-5 seconds and returns OK, but does not update status.
- Frontend disables Start while status is `Provisioning`/`Created`.
- Integration test manually forces `Provisioning -> Running -> Stopped` before testing `StartAsync`, hiding the lifecycle hole.
- README claims `Created -> Provisioning -> Running` over ~5 seconds.

**Impact:** The core assignment flow — provision a VM and see it running — is broken. New resources get stuck in `Provisioning`.

**Fix:**
- Decide lifecycle semantics:
  - If backend returns only “accepted,” add a background worker to poll/complete provisioning.
  - If mock backend is synchronous, transition directly to `Running` after backend success.
- Record a `Running` event.
- Add failing regression test: `ProvisionAsync(mock)` eventually returns/sets `Running`.
- Update UI: show progress and allow refresh; do not trap user in a state with no path forward.

---

### CRITICAL-04 — Resource detail and usage endpoints are IDOR-prone

**Files:**
- `src/Pico.Api/Endpoints/ResourceEndpoints.cs:41-80`
- `src/Pico.Application/Resources/ResourceService.cs:166-185`

**Evidence:**
- `GET /api/resources/{id}` calls `svc.GetResourceDetailAsync(id)` with no current user ID and no ownership check.
- `GET /api/resources/{id}/usage` calls `svc.GetUsageAsync(id)` with no current user ID and no ownership check.
- `GetUsageAsync` does not even load the resource; it passes `resourceId.ToString()` to the backend as the external ID.
- SSE endpoint does perform ownership check, proving the project knew the rule but didn’t apply it consistently.

**Impact:** Any authenticated user can fetch details and usage for another user’s resource if they know/guess the GUID. Usage is also functionally wrong for Docker/OpenStack because the backend expects external ID, not resource ID.

**Fix:**
- Change `GetResourceDetailAsync(resourceId, userId, isAdmin)`.
- Change `GetUsageAsync(resourceId, userId, isAdmin)` to load resource, enforce owner/admin, then call backend with `resource.ExternalId`.
- Return `403` for forbidden, `404` for missing.
- Add API tests for cross-user access.

---

### CRITICAL-05 — Cookie auth write endpoints lack CSRF protection

**Files:**
- `src/Pico.Api/Program.cs:67-87`
- `src/Pico.Api/Endpoints/AuthEndpoints.cs:23-65`
- `src/Pico.Api/Endpoints/ResourceEndpoints.cs:31-73`
- `src/Pico.Api/Endpoints/InvoiceEndpoints.cs:72-87`

**Evidence:**
- Auth uses cookies with `SameSite=Lax` and `AllowCredentials` CORS.
- POST/DELETE endpoints mutate state.
- There is no ASP.NET antiforgery token, double-submit token, origin check, or CSRF middleware.

**Impact:** Cross-site request forgery is possible for state-changing endpoints under some navigation/form scenarios. `SameSite=Lax` reduces but does not eliminate risk; it is not a CSRF strategy.

**Fix:**
- Add antiforgery token endpoint and require token on POST/DELETE, or switch API to bearer token for SPA calls.
- At minimum enforce `Origin`/`Referer` allowlist for unsafe methods.
- Add CSRF regression tests.

---

### CRITICAL-06 — OpenStack backend is a stub but documented as real

**Files:**
- `src/Pico.Infrastructure/Provisioning/OpenStackProvisioningBackend.cs:45-188`
- `src/Pico.Api/Program.cs:49-57`
- `README.md:57-60`, `README.md:113-115`
- `DESIGN.md:67-75`, `DESIGN.md:183`

**Evidence:**
- `OpenStackOptions` are not bound with `Configure<OpenStackOptions>(...)`; `Program.cs` only registers a bare singleton `OpenStackOptions`, while backend constructor asks for `IOptions<OpenStackOptions>`.
- No Nova base URL is configured. `_http.PostAsync("/servers", ...)` will not magically know the compute endpoint.
- Flavor/image refs are hardcoded: `"1"`, `"default-image"`.
- Usage returns `ResourceUsage.Empty()`.
- IP fallback returns `127.0.0.1`.
- It does not discover service catalog endpoints from Keystone.

**Impact:** OpenStack mode is not a working backend. It is a skeleton that may authenticate to Keystone but cannot reliably provision a VM.

**Fix:**
- Bind `OpenStackOptions` properly.
- Add `ComputeUrl` or discover Nova endpoint from Keystone catalog.
- Map Pico flavor/image to real OpenStack flavor/image IDs.
- Add DevStack-backed integration test or downgrade docs to “experimental stub.”

---

## High-severity findings

### HIGH-01 — Docker backend ignores flavor/image and overclaims capability

**File:** `src/Pico.Infrastructure/Provisioning/DockerProvisioningBackend.cs:27-53`

Docker backend uses fixed `alpine:3.19`, fixed 1GB RAM, fixed 1 vCPU. It does not fetch the selected flavor/image. README/DESIGN claim CPU/RAM limits match selected flavor.

**Fix:** Add flavor/image details to `ProvisionRequest` or pass a resolved backend payload from Application. Map images to container images explicitly.

---

### HIGH-02 — Database referential integrity is incomplete

**Files:**
- `src/Pico.Infrastructure/Persistence/Configurations/UserConfiguration.cs:61-171`
- `src/Pico.Infrastructure/Persistence/Migrations/20260630_InitialCreate.cs:76-198`
- `DESIGN.md:122-130`

**Evidence:**
- Resources have FK constraints only to `flavors` and `images`, not `users`.
- `resource_events.resource_id` has no FK to `resources`.
- `invoices.user_id` has no FK to `users`.
- `invoice_lines.resource_id` / `flavor_id` have no FK.
- DESIGN claims `resources.user_id -> users` and “all with `OnDelete(Restrict)`,” which is false.

**Impact:** Orphan resources/invoices/events can exist. Data model docs overclaim correctness.

**Fix:** Add missing EF relationships and generated migration. Do not hand-maintain snapshot.

---

### HIGH-03 — Integration tests fail and are not a quality gate

**Files:**
- `tests/Pico.Tests/Integration/PostgresFixture.cs:18-32`
- `tests/Pico.Tests/Integration/RepositoryIntegrationTests.cs`
- `README.md:177-195`

**Evidence:** `dotnet test --filter "FullyQualifiedName~Integration"` fails 5/5 with Docker BadRequest: invalid port specification `"0/tcp"`.

Root cause is likely `.WithPortBinding(0, true)` in the PostgreSQL Testcontainers builder.

**Impact:** The “real Postgres integration” claim is not true today. More importantly, the tests would have caught EF/migration issues if they actually ran.

**Fix:** Use the Testcontainers PostgreSQL builder defaults, or bind container port 5432 correctly. Run these in CI.

---

### HIGH-04 — Frontend tests, E2E tests, and lint are missing/broken

**Files:**
- `frontend/package.json:5-14`
- `README.md:196`
- `openspec/changes/pico-self-service-cloud/tasks.md:81-86`

**Evidence:**
- `npm test -- --run` fails: no test files.
- `npm run e2e` fails: no Playwright tests.
- `npm run lint` fails under Next 16: `next lint` invalid.

**Impact:** There is no automated frontend confidence despite many interactive routes.

**Fix:**
- Replace `next lint` with `eslint .` and add ESLint config compatible with Next 16.
- Add Vitest component tests for login/signup/catalog/provision/resource detail.
- Add Playwright smoke: signup → catalog → provision → resource detail.

---

### HIGH-05 — Pre-commit hook is not reproducible and does not meet project standard

**Files:**
- `.gitignore`
- local `.git/hooks/pre-commit` only; not tracked
- `openspec/changes/pico-self-service-cloud/tasks.md:85`

**Evidence:**
- `git ls-files .git/hooks/pre-commit` returns nothing.
- Hook is installed only in local `.git/hooks`, not in repo.
- It does not run ESLint or Vitest; it also ignores integration tests.

**Impact:** Reviewer clone does not get the hook. The repo does not satisfy “pre-commit hook that lints every file.”

**Fix:** Use `pre-commit` framework, Husky, Lefthook, or `scripts/pre-commit.sh` tracked in repo with install instructions. It should run dotnet format, dotnet tests, eslint, TypeScript, Vitest.

---

### HIGH-06 — NuGet audit disabled while vulnerable packages are present

**Files:**
- `Directory.Build.props:7-10`
- `src/Pico.Api/Pico.Api.csproj`
- `src/Pico.Infrastructure/Pico.Infrastructure.csproj`

**Evidence:**
- `Directory.Build.props` sets `TreatWarningsAsErrors=false`, `NuGetAudit=false`, and suppresses `NU1903`.
- `dotnet list package --vulnerable --include-transitive` reports high severity transitive CVEs:
  - `Microsoft.OpenApi 2.0.0`
  - `System.Security.Cryptography.Xml 9.0.0`

**Impact:** Security warnings are hidden globally. This is bad optics and bad practice for a Lead Full-Stack take-home.

**Fix:** Upgrade packages or add narrow suppressions with comments and tracking references. Do not blanket-disable audit.

---

### HIGH-07 — Auth is insecure beyond “mocked” expectations

**Files:**
- `src/Pico.Api/Endpoints/AuthEndpoints.cs:32-55`
- `src/Pico.Api/Program.cs:71-76`

**Issues:**
- Passwords hashed with unsalted SHA256 and fake `argon2id$dev$` prefix.
- Exact string compare, not constant-time.
- No password policy besides signup frontend min length 6; API only checks non-empty.
- No login rate limit, account lockout, or audit logging.
- `CookieSecurePolicy.SameAsRequest` can emit non-secure cookies if app is reached over HTTP or proxy headers are not configured.

**Fix:** Use ASP.NET Core Identity password hasher or Argon2id. Add API validation, rate limiting, audit log, secure cookies in production, forwarded headers if behind proxy.

---

### HIGH-08 — Invoice generation/payment is barely wired

**Files:**
- `src/Pico.Application/Billing/InvoiceGenerator.cs`
- `src/Pico.Api/Endpoints/InvoiceEndpoints.cs`
- `src/Pico.Infrastructure/Seed/DataSeeder.cs`

**Evidence:**
- There is no background invoice generation job.
- Seeder does not create sample resources/invoices despite OpenSpec task `2.7` saying sample resources/invoices.
- Billing pages will usually be empty.
- `pay` endpoint marks paid with no idempotency key, no audit log, no simulated transaction record.

**Impact:** Billing is UI/API surface without actual workflow.

**Fix:** Seed demo invoices, add monthly invoice generation service or explicit admin/demo trigger, audit payment state changes.

---

## Medium-severity findings

### MEDIUM-01 — API error handling is not real RFC 7807 ProblemDetails

**Files:**
- `src/Pico.Api/Program.cs:101-121`
- `README.md:72`, `DESIGN.md:12`

It writes an anonymous object. It is not `ProblemDetails`, does not set `application/problem+json`, and does not use `UseExceptionHandler`/`AddProblemDetails`.

---

### MEDIUM-02 — `AuthEndpoints.GetCurrentUser` blocks on async and leaks a scope

**File:** `src/Pico.Api/Endpoints/AuthEndpoints.cs:92-100`

Creates a scope and never disposes it, then blocks with `.GetAwaiter().GetResult()`. This can cause thread starvation and resource leaks. Use async endpoint and scoped `IUserRepository` injection instead.

---

### MEDIUM-03 — Sync `SaveChanges()` inside async request flow

**File:** `src/Pico.Infrastructure/Repositories/Repositories.cs:95-99`, `134-138`

Repository `Update` methods call synchronous `SaveChanges()`. In ASP.NET request paths this blocks threads. Use async `UpdateAsync`/`SaveChangesAsync`.

---

### MEDIUM-04 — Request validation is shallow / missing

**Files:**
- `src/Pico.Api/Pico.Api.csproj:18`
- `src/Pico.Api/Endpoints/*.cs`

FluentValidation package exists but no validators are registered or used. API accepts weak/blank names, no max-length validation before DB save, no password length validation server-side, no DTO-level validation.

---

### MEDIUM-05 — CORS is broad for credentialed cookie auth

**File:** `src/Pico.Api/Program.cs:19-30`

Credentialed CORS with `AllowAnyHeader`, `AllowAnyMethod`, and wildcard subdomains increases blast radius. Use exact origin list per environment and restrict unsafe methods where possible.

---

### MEDIUM-06 — Frontend home-page auth redirect is misleading/static

**Files:**
- `frontend/src/app/page.tsx:7-14`
- `frontend/src/lib/api.ts:7`
- `frontend/next.config.ts:8-11`

Home page is built as static (`○ /`) despite server-side `auth.me()` call. Cookies are not forwarded; API client uses `NEXT_PUBLIC_API_URL`, not internal `API_URL`. The “server-side check” is not reliable.

---

### MEDIUM-07 — Mobile layout is not actually responsive

**Files:**
- `frontend/src/app/(dashboard)/layout.tsx:30-38`
- `frontend/src/components/Sidebar.tsx:25-72`

Dashboard uses fixed left sidebar `w-60`, `h-screen`, sticky top, no mobile collapse/drawer. Tables do not have horizontal scroll wrappers. Docs claim mobile-responsive; actual layout will be poor on phones.

---

### MEDIUM-08 — Resource detail imports charts but renders none

**File:** `frontend/src/app/(dashboard)/resources/[id]/page.tsx:8-10`, `97-100`

`recharts` imports and `usageData` are unused. README says usage charts; actual UI is three metric cards. Either render chart or remove claim/imports.

---

### MEDIUM-09 — Tracked build artifact causes dirty working tree

**File:** `frontend/tsconfig.tsbuildinfo`

This file is tracked and becomes modified by `tsc --noEmit`. It should be ignored and removed from git.

---

### MEDIUM-10 — OpenSpec task list is all unchecked and not archived

**File:** `openspec/changes/pico-self-service-cloud/tasks.md`

Every task remains `[ ]`, including tasks claimed complete. This makes spec-driven development look theatrical rather than disciplined.

---

## Low-severity / polish findings

### LOW-01 — Template leftovers and empty files

Tracked files:
- `src/Pico.Application/Class1.cs`
- `src/Pico.Infrastructure/Class1.cs`
- `src/Pico.Application/Catalog/ResourceService.cs` (empty)

Remove them.

### LOW-02 — Manual migration/snapshot is risky

Files:
- `src/Pico.Infrastructure/Persistence/Migrations/20260630_InitialCreate.cs`
- `src/Pico.Infrastructure/Persistence/Migrations/PicoDbContextModelSnapshot.cs`

Comments say manually written because design-time tooling was not set up. For a take-home, this is acceptable only if tests verify it. They currently fail.

### LOW-03 — README/AI_USAGE overstate verification

Examples:
- README says “complete, working” and “That’s it” for broken quickstart.
- AI_USAGE says every commit passed tests; integration/frontend/e2e/Docker were not passing.
- DESIGN says Docker/OpenStack modes prove the design is real; they do not currently.

Tone these down or fix the underlying issues.

---

## Cheating / unplanned stubs / overclaim audit

These are the specific places that look like “we wanted the badge without doing the work”:

1. **OpenStack backend** — hardcoded `"1"`, `"default-image"`, no Nova base URL, usage empty. Documented as real.
2. **Docker backend** — claims flavor-matched CPU/RAM but hardcodes 1 vCPU/1GB and Alpine image.
3. **Provisioning lifecycle** — no real completion to `Running`; tests manually force states.
4. **Integration tests** — present in repo but fail immediately; README still lists them as coverage.
5. **Frontend/E2E tests** — scripts exist but no tests; README says scaffolded, OpenSpec says they should exist.
6. **Pre-commit** — local-only hook, not in repo, doesn’t lint every file.
7. **Manual EF migration** — hand-written snapshot without green integration tests.
8. **Demo seed** — comments/docs imply seeded demo users, but config mismatch prevents it, and hashes are incompatible.
9. **Charts** — Recharts imported but not rendered; docs say usage charts.
10. **ProblemDetails** — docs claim RFC 7807; code writes anonymous JSON.

---

## Additional confirmed findings from parallel review

These came from the background backend/frontend/deployment review and are worth preserving in the main report.

### ADDITIONAL-HIGH-01 — Invalid nested interactive controls in frontend

**Files:**
- `frontend/src/components/ui/Button.tsx:35-41`
- `frontend/src/app/page.tsx:22-27`, `39-47`
- `frontend/src/app/(dashboard)/dashboard/page.tsx:31-36`, `45-50`, `94-96`
- `frontend/src/app/(dashboard)/billing/page.tsx:93-98`

Many places render `<Link><Button /></Link>`, while `Button` renders a native `<button>`. That creates invalid anchor/button nesting, bad keyboard behavior, and poor screen-reader semantics.

**Fix:** Add an `asChild`/Slot-style Button variant, or use styled links for navigation actions.

### ADDITIONAL-HIGH-02 — Frontend query error states are missing

**Files:**
- `frontend/src/app/(dashboard)/dashboard/page.tsx:15-20`
- `frontend/src/app/(dashboard)/catalog/page.tsx:14-20`
- `frontend/src/app/(dashboard)/resources/[id]/page.tsx:24-39`, `89`
- `frontend/src/app/(dashboard)/billing/[id]/page.tsx:19-34`
- `frontend/src/app/(dashboard)/admin/page.tsx:12-22`
- `frontend/src/app/(dashboard)/health/page.tsx:10-16`

Most failed API calls render empty tables, permanent spinners, or generic states. A 403 admin response and a 404 resource response should not look like the app is still loading.

**Fix:** Add explicit `isError`/`error` states per page, with 403/404-aware messages and retry actions.

### ADDITIONAL-HIGH-03 — SSE events duplicate on resource detail

**File:** `frontend/src/app/(dashboard)/resources/[id]/page.tsx:46-50`, `91`, `201-202`

The backend SSE stream sends existing events first. The frontend appends those to local `events`, then renders `detail.events + events`, duplicating historical events and potentially duplicating React keys.

**Fix:** Keep one source of truth, or de-dupe events by ID before rendering.

### ADDITIONAL-HIGH-04 — Destructive terminate action has no confirmation

**File:** `frontend/src/app/(dashboard)/resources/[id]/page.tsx:138-141`

A single click terminates a resource. No confirm dialog, no typed resource name, no undo, no second chance. For a cloud resource management product, that is hostile.

**Fix:** Add a confirmation dialog; require resource name for terminate if we want a serious admin/customer UX.

### ADDITIONAL-HIGH-05 — Provisioning backend DI is likely broken for docker/openstack modes

**Files:**
- `src/Pico.Api/Program.cs:51-52`
- `src/Pico.Infrastructure/Provisioning/ProvisioningBackendFactory.cs:15-33`

`AddScoped<Lazy<DockerProvisioningBackend>>()` / `Lazy<OpenStackProvisioningBackend>` is not a safe DI pattern here. Evaluating `.Value` may bypass the expected registered dependency graph. Docker/OpenStack modes need startup/DI tests.

**Fix:** Register `Func<T>` factories or inject `IServiceProvider` deliberately into the factory and resolve named implementations there.

### ADDITIONAL-HIGH-06 — Signup does not auto-login despite README claim

**Files:**
- `README.md:37`
- `src/Pico.Api/Endpoints/AuthEndpoints.cs:23-41`

README says signup auto-logs in. Signup endpoint only creates and returns a user DTO. It never calls `SignInAsync`.

**Fix:** Either auto-login in signup or change README/UI expectations.

### ADDITIONAL-MEDIUM-01 — Failed provisioning leaves orphaned `Created` resources

**File:** `src/Pico.Application/Resources/ResourceService.cs:68-83`

The resource is persisted before backend provisioning. If backend provisioning fails, the method returns failure but leaves a `Created` resource with no external ID and no failure event.

**Fix:** Use a transaction and roll back, or transition to `Failed` and record a failure event.

### ADDITIONAL-MEDIUM-02 — Start/stop/terminate mutate state before backend success

**File:** `src/Pico.Application/Resources/ResourceService.cs:105-157`

State transitions happen before backend operations. If the backend fails after the entity is mutated, EF tracking can leave inconsistent state in the context. `ExternalId` null checks also happen after transition.

**Fix:** Validate preconditions first, call backend, then persist state transition in a transaction.

### ADDITIONAL-MEDIUM-03 — Catalog/provisioning does not validate flavor/image existence or active status

**File:** `src/Pico.Application/Resources/ResourceService.cs:65-77`

Provisioning accepts flavor/image GUIDs without checking that they exist or are active. Invalid IDs fall through to DB FK failures or backend calls; inactive flavors/images can still be used.

**Fix:** Inject catalog repositories into resource service and validate flavor/image before resource creation.

### ADDITIONAL-MEDIUM-04 — Invoice line counts are likely wrong in list endpoints

**Files:**
- `src/Pico.Infrastructure/Repositories/Repositories.cs:122-126`
- `src/Pico.Api/Endpoints/InvoiceEndpoints.cs:92-94`

Invoice list queries do not include `Lines`, but DTO uses `i.Lines.Count`. EF lazy loading is not enabled, so `LineCount` may be `0` even when invoice lines exist.

**Fix:** Include lines or project count in the query.

### ADDITIONAL-MEDIUM-05 — `InvoiceGenerator` bypasses invoice-line invariant with `Guid.Empty`

**File:** `src/Pico.Application/Billing/InvoiceGenerator.cs:34-37`

The public `InvoiceLine` factory rejects empty invoice IDs, but `InvoiceGenerator` uses an internal constructor with `Guid.Empty` and relies on EF relationship fix-up. That needs persistence tests or a cleaner aggregate API.

### ADDITIONAL-MEDIUM-06 — No audit logging is wired

**Files:**
- `src/Pico.Domain/Entities/AuditLog.cs`
- `src/Pico.Infrastructure/Repositories/Repositories.cs:141-154`

Audit table/repository exist, but endpoints/services do not write audit logs for login, failed login, resource lifecycle, payment, or admin reads.

**Fix:** Add audit calls at security/resource/payment boundaries.

### ADDITIONAL-MEDIUM-07 — Homepage/catalog auth and URL contract are confused

**Files:**
- `frontend/src/app/page.tsx:7-14`, `45-47`
- `frontend/src/lib/api.ts:6-7`
- `frontend/next.config.ts:8-10`
- `frontend/src/app/(dashboard)/layout.tsx:14-17`

The landing page links unauthenticated users to `/catalog`, but `/catalog` is under the auth-gated dashboard layout. Also the server page calls the same public browser API URL without forwarding cookies or using the internal `API_URL`.

**Fix:** Make catalog public or change CTA; separate server/internal API base URL from browser API base URL.

### ADDITIONAL-MEDIUM-08 — Render-phase state update in provisioning page

**File:** `frontend/src/app/(dashboard)/catalog/[id]/page.tsx:33-36`

The page calls `setImageId` during render to select a default image. That is a React anti-pattern and can misbehave under Strict Mode/future React behavior.

**Fix:** Use `useEffect` or derive default selected image without state mutation during render.

### ADDITIONAL-MEDIUM-09 — Frontend mobile/responsive UX is weaker than docs claim

**Files:**
- `frontend/src/components/Sidebar.tsx:26`
- `frontend/src/app/(dashboard)/layout.tsx:37`
- table pages under dashboard/billing/admin

Dashboard has a fixed-width sticky sidebar and wide tables without overflow wrappers. This is not credibly mobile-responsive.

**Fix:** Add mobile nav/drawer, responsive padding, and horizontal scroll containers for tables.

### ADDITIONAL-MEDIUM-10 — Docker/prod frontend build is not reproducible

**Files:**
- `frontend/Dockerfile.prod:6-8`, `25`

Prod Dockerfile runs `npm install` from `package.json` only and ignores `package-lock.json`. It also copies `/app/public` even though no `frontend/public` exists.

**Fix:** Copy lockfile and use `npm ci`; create `public/` or conditionally copy it.

### ADDITIONAL-LOW-01 — Health and event contracts are misleading

**Files:**
- `frontend/src/app/(dashboard)/health/page.tsx:32-48`
- `frontend/src/lib/api.ts:123-129`

Health page hardcodes API value/status instead of using backend status and displays “Last fetched” using current render time. `ResourceEvent` frontend type expects `type`, while REST detail events use `eventType`; SSE uses `type`.

**Fix:** Align DTO types and render real status/fetch timestamp.

### ADDITIONAL-LOW-02 — Form validation lacks accessibility bindings

**Files:**
- `frontend/src/app/(auth)/login/page.tsx:54-61`
- `frontend/src/app/(auth)/signup/page.tsx:54-66`

Errors are visible text but not connected to inputs with `aria-invalid` and `aria-describedby`.

---

## Recommended fix plan

### Pass 1 — Make reviewer quickstart real

1. Fix Dockerfiles:
   - UID/GID safe user creation.
   - Backend prod uses `Pico.slnx` or publishes project directly.
   - Frontend prod handles missing `public/`.
2. Fix compose:
   - Add local ports.
   - Remove/optionalize external networks for local mode.
   - API dev mount repo root.
   - Local `NEXT_PUBLIC_API_URL=http://localhost:8080`.
3. Run and verify:
   - `docker compose up --build`
   - browser opens localhost frontend/API.

### Pass 2 — Fix demo/auth/provisioning core flow

1. Align seed config and hash demo users with the real hasher.
2. Replace SHA256 with ASP.NET password hasher or Argon2id.
3. Make mock provisioning transition to `Running` or add background worker.
4. Add API smoke/integration tests for signup/login/provision/detail.

### Pass 3 — Fix security basics

1. Add CSRF protection or switch SPA auth strategy.
2. Fix resource detail/usage ownership checks.
3. Add rate limiting for login/signup.
4. Force secure cookies in production and configure forwarded headers.
5. Upgrade vulnerable NuGet/npm packages; remove blanket audit suppression.

### Pass 4 — Make tests honest

1. Fix Testcontainers Postgres fixture.
2. Add endpoint tests with WebApplicationFactory.
3. Add Vitest component tests.
4. Add Playwright smoke test.
5. Replace lint script with working ESLint.
6. Add tracked pre-commit config/script.

### Pass 5 — Truthful docs/specs

1. Update README with actual local/deploy commands.
2. Downgrade OpenStack/Docker wording until verified.
3. Mark OpenSpec tasks complete only after verification.
4. Remove inflated “production-grade” claims not backed by code.

---

## Positive notes worth preserving

- Domain model is clean and dependency-light.
- Resource state machine is a good design choice.
- 91 unit tests are fast and valuable.
- Application/Infrastructure separation is mostly sound.
- Minimal UI direction is aligned with user preference.
- SSE approach is reasonable for one-way status events, once the lifecycle is fixed.

The bones are good. The problem is the flesh currently has stage makeup on it.