# PICO Implementation Plan (MoA-generated, gpt-5.5 aggregated)

> Generated via Mixture of Agents: 6 reference models (minimax-m3, kimi-k2.7-code,
> glm-5.2, nemotron-3-ultra-550b, north-mini-code, qwen3.7-plus) + gpt-5.5 aggregator
> via openai-codex

## Structure

11 phases, 152 tasks, stable IDs (PIC-001 through PIC-152).
Each task: 2-5 min, exact file paths, copy-pasteable code, TDD verification.

---

## Phase 1 — Project Scaffolding (PIC-001 → PIC-015)

| ID | Task | Verify |
|----|------|--------|
| PIC-001 | Initialize .gitignore (bin/, obj/, node_modules/, .next/, .env, etc.) | `git status` shows clean |
| PIC-002 | Create .NET 10 solution + 4 projects (Domain, Application, Infrastructure, Api) | `dotnet build` succeeds |
| PIC-003 | Add project references (Domain ← Application ← Infrastructure ← Api) | `dotnet build` succeeds |
| PIC-004 | Create Directory.Build.props (nullable, implicit usings, LangVersion latest) | `dotnet build` succeeds |
| PIC-005 | Create Next.js 16 frontend with Tailwind 4 | `npm run dev` starts |
| PIC-006 | Install frontend deps (shadcn/ui, TanStack Query, Zod, Recharts, next-themes, lucide-react) | `npm run build` succeeds |
| PIC-007 | Create backend Dockerfile.dev (non-root user pixu, dotnet watch) | `docker build` succeeds |
| PIC-008 | Create frontend Dockerfile.dev (non-root user pixu, npm run dev) | `docker build` succeeds |
| PIC-009 | Create compose.yaml (postgres + api + frontend, Caddy labels) | `docker compose up` starts |
| PIC-010 | Create .env.example with all env vars | file exists, documented |
| PIC-011 | Add NuGet packages (EF Core 10, FluentValidation, Testcontainers, xUnit) | `dotnet restore` succeeds |
| PIC-012 | Create backend test project (Pico.Tests) with xUnit + Testcontainers | `dotnet test` runs (0 tests) |
| PIC-013 | Create frontend test setup (Vitest + Testing Library + Playwright) | `npx vitest run` runs |
| PIC-014 | Create pre-commit hook (dotnet format + tsc + eslint + vitest) | hook runs on commit |
| PIC-015 | Create AGENTS.md + README.md skeleton | file exists |

---

## Phase 2 — Domain Layer (PIC-016 → PIC-030)

| ID | Task | Verify |
|----|------|--------|
| PIC-016 | Write User entity tests (creation, role assignment) | tests fail (no entity) |
| PIC-017 | Implement User entity (id, email, name, role, passwordHash, createdAt) | tests pass |
| PIC-018 | Write Flavor entity tests (creation, pricing calculation) | tests fail |
| PIC-019 | Implement Flavor entity (id, name, vcpus, ramMb, diskGb, pricePerHour, pricePerMonth, category, active) | tests pass |
| PIC-020 | Write Image entity tests | tests fail |
| PIC-021 | Implement Image entity (id, name, os, version, sizeGb, active) | tests pass |
| PIC-022 | Write ResourceStatus value object tests (all valid transitions) | tests fail |
| PIC-023 | Implement ResourceStatus enum + state machine transition logic | tests pass |
| PIC-024 | Write Resource entity tests (creation, status transitions) | tests fail |
| PIC-025 | Implement Resource entity (id, userId, flavorId, imageId, name, status, externalId, ipAddress) | tests pass |
| PIC-026 | Write ResourceEvent entity tests | tests fail |
| PIC-027 | Implement ResourceEvent entity (id, resourceId, eventType, oldStatus, newStatus, message, timestamp) | tests pass |
| PIC-028 | Write Invoice + InvoiceLine entity tests | tests fail |
| PIC-029 | Implement Invoice + InvoiceLine entities | tests pass |
| PIC-030 | Write AuditLog entity + tests | tests pass |

### Resource State Machine

```
Created → Provisioning → Running ⇄ Stopped → Terminated
                ↓
            Failed
```

Valid transitions:
- Created → Provisioning
- Provisioning → Running
- Provisioning → Failed
- Running → Stopped
- Stopped → Running
- Running → Terminated
- Stopped → Terminated
- Failed → Terminated

---

## Phase 3 — Application Layer (PIC-031 → PIC-047)

| ID | Task | Verify |
|----|------|--------|
| PIC-031 | Write ResourceStateMachine tests (all valid transitions) | tests fail |
| PIC-032 | Write ResourceStateMachine tests (all invalid transitions → DomainException) | tests fail |
| PIC-033 | Implement ResourceStateMachine | tests pass |
| PIC-034 | Write PricingCalculator tests (hourly estimate) | tests fail |
| PIC-035 | Write PricingCalculator tests (monthly estimate with discount) | tests fail |
| PIC-036 | Implement PricingCalculator | tests pass |
| PIC-037 | Write InvoiceGenerator tests (line items from resource usage) | tests fail |
| PIC-038 | Write InvoiceGenerator tests (no active resources → no invoice) | tests fail |
| PIC-039 | Implement InvoiceGenerator | tests pass |
| PIC-040 | Define IProvisioningBackend interface | `dotnet build` succeeds |
| PIC-041 | Define DTOs (ProvisionRequest, ProvisionResult, ResourceUsage, BackendHealth) | build succeeds |
| PIC-042 | Define API DTOs (FlavorDto, ImageDto, ResourceDto, InvoiceDto, etc.) | build succeeds |
| PIC-043 | Write mapping profiles (entity → DTO) | build succeeds |
| PIC-044 | Implement mappers (manual or Mapster) | build succeeds |
| PIC-045 | Define IResourceRepository, IInvoiceRepository, IUserRepository interfaces | build succeeds |
| PIC-046 | Write FluentValidation rules for all request DTOs | build succeeds |
| PIC-047 | Implement validators | build succeeds |

### IProvisioningBackend interface

```csharp
public interface IProvisioningBackend
{
    Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct);
    Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct);
    Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct);
    Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct);
    Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct);
    Task<BackendHealth> GetHealthAsync(CancellationToken ct);
}
```

---

## Phase 4 — Infrastructure Layer (PIC-048 → PIC-068)

| ID | Task | Verify |
|----|------|--------|
| PIC-048 | Write PicoDbContext tests (can create, query) | tests fail |
| PIC-049 | Implement PicoDbContext with DbSet properties | tests pass |
| PIC-050 | Write EF Core Fluent API configs (User) | build succeeds |
| PIC-051 | Write EF Core Fluent API configs (Flavor, Image) | build succeeds |
| PIC-052 | Write EF Core Fluent API configs (Resource, ResourceEvent) | build succeeds |
| PIC-053 | Write EF Core Fluent API configs (Invoice, InvoiceLine, AuditLog) | build succeeds |
| PIC-054 | Create initial EF Core migration | `dotnet ef migrations add Initial` succeeds |
| PIC-055 | Write MockProvisioningBackend tests (provision → Running) | tests fail |
| PIC-056 | Write MockProvisioningBackend tests (start/stop/terminate, usage) | tests fail |
| PIC-057 | Implement MockProvisioningBackend (DB-only, 2-5s delay, simulated usage) | tests pass |
| PIC-058 | Write DockerProvisioningBackend tests | tests fail |
| PIC-059 | Implement DockerProvisioningBackend (Docker API, container lifecycle, CPU/RAM limits) | tests pass |
| PIC-060 | Write OpenStackProvisioningBackend tests | tests fail |
| PIC-061 | Implement OpenStackProvisioningBackend (Nova API calls via HTTP) | tests pass |
| PIC-062 | Write ProvisioningBackendFactory tests (mode selection via env var) | tests fail |
| PIC-063 | Implement ProvisioningBackendFactory | tests pass |
| PIC-064 | Write background provisioning service tests | tests fail |
| PIC-065 | Implement ProvisioningWorker (queued requests → backend → status update) | tests pass |
| PIC-066 | Write seed data tests (flavors, images, demo users) | tests fail |
| PIC-067 | Implement DataSeeder (6 flavors, 4 images, 2 users, sample resources/invoices) | tests pass |
| PIC-068 | Implement auto-migrate on startup (IHostedService) | app starts, DB migrated |

---

## Phase 5 — API Layer (PIC-069 → PIC-088)

| ID | Task | Verify |
|----|------|--------|
| PIC-069 | Write auth middleware tests | tests fail |
| PIC-070 | Implement cookie auth middleware + role authorization | tests pass |
| PIC-071 | Write AuthEndpoints tests (signup) | tests fail |
| PIC-072 | Write AuthEndpoints tests (login, logout, me) | tests fail |
| PIC-073 | Implement AuthEndpoints | tests pass |
| PIC-074 | Write CatalogEndpoints tests (flavors, images) | tests fail |
| PIC-075 | Implement CatalogEndpoints | tests pass |
| PIC-076 | Write ResourceEndpoints tests (create, list, detail) | tests fail |
| PIC-077 | Write ResourceEndpoints tests (start, stop, terminate) | tests fail |
| PIC-078 | Write ResourceEndpoints tests (SSE events stream) | tests fail |
| PIC-079 | Implement ResourceEndpoints + SSE stream | tests pass |
| PIC-080 | Write InvoiceEndpoints tests (list, detail, pay) | tests fail |
| PIC-081 | Implement InvoiceEndpoints | tests pass |
| PIC-082 | Write HealthEndpoints tests | tests fail |
| PIC-083 | Implement HealthEndpoints (backend status, DB connectivity) | tests pass |
| PIC-084 | Write AdminEndpoints tests (users, resources, metrics) | tests fail |
| PIC-085 | Implement AdminEndpoints | tests pass |
| PIC-086 | Write CORS tests (whitelisted origins) | tests fail |
| PIC-087 | Implement CORS (pico.aamar.cloud, pico.ski.bd, localhost:3000) | tests pass |
| PIC-088 | Implement global exception handler + problem details | error responses are RFC 7807 |

### API Endpoints

```
POST   /api/auth/signup, /api/auth/login, /api/auth/logout
GET    /api/auth/me
GET    /api/catalog/flavors, /api/catalog/flavors/{id}, /api/catalog/images
GET    /api/resources, POST /api/resources, GET /api/resources/{id}
POST   /api/resources/{id}/start, /api/resources/{id}/stop, DELETE /api/resources/{id}
GET    /api/resources/{id}/usage, GET /api/resources/{id}/events (SSE)
GET    /api/invoices, GET /api/invoices/{id}, POST /api/invoices/{id}/pay
GET    /api/health
GET    /api/admin/users, /api/admin/resources, /api/admin/metrics
```

---

## Phase 6 — Frontend Foundation (PIC-089 → PIC-100)

| ID | Task | Verify |
|----|------|--------|
| PIC-089 | Create app layout (root, auth, dashboard route groups) | `npm run dev` renders layout |
| PIC-090 | Implement API client with typed fetch + TanStack Query | type-safe, no errors |
| PIC-091 | Implement auth context + hooks (useAuth, useUser) | context works |
| PIC-092 | Implement login form with Zod validation | form validates correctly |
| PIC-093 | Implement signup form with Zod validation | form validates correctly |
| PIC-094 | Implement theme provider (light/dark mode via next-themes) | theme toggle works |
| PIC-095 | Create shared UI components (Button, Card, Badge, Input, Table) | components render |
| PIC-096 | Create StatusBadge component (resource status colors) | badge shows correct color |
| PIC-097 | Create EmptyState component (icon + message + CTA) | renders correctly |
| PIC-098 | Create LoadingSpinner / Skeleton components | renders correctly |
| PIC-099 | Create ErrorBoundary + error display | catches errors gracefully |
| PIC-100 | Create Layout (sidebar nav, header, main content area) | layout renders on all breakpoints |

---

## Phase 7 — Frontend Pages (PIC-101 → PIC-118)

| ID | Task | Verify |
|----|------|--------|
| PIC-101 | Build landing/catalog page (flavor cards, pricing, CTA to sign up) | page renders seed flavors |
| PIC-102 | Build provisioning wizard (select flavor → select image → name → confirm → provision) | wizard completes provisioning |
| PIC-103 | Build dashboard (resource list with status badges, quick actions) | list shows user's resources |
| PIC-104 | Build resource detail page (status, specs, action buttons) | page renders resource detail |
| PIC-105 | Build usage charts (Recharts: CPU, RAM, disk, network gauges) | charts render with mock data |
| PIC-106 | Build events timeline component (SSE live updates, fallback polling) | SSE updates status in real-time |
| PIC-107 | Build billing page (invoice list with status badges) | invoices display correctly |
| PIC-108 | Build invoice detail page (line items, total, pay button) | invoice detail renders |
| PIC-109 | Build health/status page (backend mode, DB status, uptime) | page shows backend status |
| PIC-110 | Build admin dashboard (metrics cards: users, resources, revenue) | admin sees aggregate data |
| PIC-111 | Build admin resource management page (all resources, filter) | filter works |
| PIC-112 | Implement loading/error/empty states for all pages | each state renders correctly |
| PIC-113 | Implement responsive mobile layout (mobile-first breakpoints) | mobile layout correct at 375px |
| PIC-114 | Implement dark mode across all pages | dark mode looks correct |
| PIC-115 | Write Vitest component tests (catalog) | tests pass |
| PIC-116 | Write Vitest component tests (provisioning wizard) | tests pass |
| PIC-117 | Write Vitest component tests (resource detail) | tests pass |
| PIC-118 | Write Vitest component tests (billing) | tests pass |

---

## Phase 8 — Docker & Deployment (PIC-119 → PIC-127)

| ID | Task | Verify |
|----|------|--------|
| PIC-119 | Finalize backend Dockerfile.dev (non-root, hot reload via dotnet watch) | container builds and runs |
| PIC-120 | Finalize frontend Dockerfile.dev (non-root, hot reload) | container builds and runs |
| PIC-121 | Finalize compose.yaml (postgres + api + frontend + Caddy labels) | full stack starts |
| PIC-122 | Write .env.example with all required env vars | documented in README |
| PIC-123 | Test `docker compose up --build` (full stack starts from clean) | all services healthy |
| PIC-124 | Verify Caddy routing (pico.aamar.cloud → frontend, pico-api.aamar.cloud → API) | routes work |
| PIC-125 | Verify CORS headers (preflight OPTIONS returns correct headers) | CORS works |
| PIC-126 | Test self-contained reviewer experience: `git clone && docker compose up --build` | app works with zero extra config |
| PIC-127 | Write production Dockerfile (multi-stage, optimized, no SDK) | image builds, app runs |

---

## Phase 9 — Integration & E2E Testing (PIC-128 → PIC-137)

| ID | Task | Verify |
|----|------|--------|
| PIC-128 | Write integration test: auth lifecycle (signup → login → me → logout) | test passes with Testcontainers PG |
| PIC-129 | Write integration test: provisioning lifecycle (provision → stop → start → terminate) | test passes |
| PIC-130 | Write integration test: billing lifecycle (provision → invoice → pay) | test passes |
| PIC-131 | Write integration test: admin endpoints (RBAC — customer can't access admin) | test passes |
| PIC-132 | Write integration test: SSE events stream (provision → receive events) | test passes |
| PIC-133 | Write Playwright E2E: signup → provision → view → pay (full smoke) | E2E passes |
| PIC-134 | Write Playwright E2E: dark mode toggle | E2E passes |
| PIC-135 | Write Playwright E2E: mobile responsive (375px viewport) | E2E passes |
| PIC-136 | Write regression tests (one per known bug found during development) | all pass |
| PIC-137 | Run full test suite + lint → all green | `dotnet test` + `npx vitest run` + `npx playwright test` all pass |

---

## Phase 10 — Documentation (PIC-138 → PIC-146)

| ID | Task | Verify |
|----|------|--------|
| PIC-138 | Write README.md (what, how to run, demo creds, key flows) | reviewer can follow instructions |
| PIC-139 | Write DESIGN.md (architecture overview, tradeoffs, assumptions) | explains key decisions |
| PIC-140 | Write AI_USAGE.md (AI tools used, review process, ownership) | honest reflection |
| PIC-141 | Document data model in README (entity diagram) | data model clear |
| PIC-142 | Document API endpoints in README | API surface clear |
| PIC-143 | Document known limitations | limitations honest |
| PIC-144 | Document "what I would improve with more time" | improvements thoughtful |
| PIC-145 | Final commit + push to GitHub | repo is clean, pushed |
| PIC-146 | Make repo public, verify reviewer experience | `git clone && docker compose up --build` works |

---

## Phase 11 — DevStack/OpenStack Integration (PIC-147 → PIC-152, stretch)

| ID | Task | Verify |
|----|------|--------|
| PIC-147 | Complete DevStack installation on KVM VM (192.168.122.43) | `openstack service list` shows all services |
| PIC-148 | Configure OpenStackProvisioningBackend with VM credentials (OS_AUTH_URL, etc.) | can list Nova flavors |
| PIC-149 | Create Nova flavors matching Pico catalog (m1.tiny, m1.small, m1.medium, etc.) | flavors visible in Horizon |
| PIC-150 | Test end-to-end provisioning via OpenStack mode (provision → real VM created) | VM visible in Horizon dashboard |
| PIC-151 | Document DevStack setup in README (how to reproduce) | instructions reproducible |
| PIC-152 | Verify real VM creation, stop/start, terminate via Nova API | lifecycle works end-to-end |

---

## Domain Model Reference

```
User          (id, email, name, role[Customer|Admin], password_hash, created_at)
Flavor        (id, name, vcpus, ram_mb, disk_gb, price_per_hour, price_per_month, category, active)
Image         (id, name, os, version, size_gb, active)
Resource      (id, user_id, flavor_id, image_id, name, status, external_id, ip_address, created_at, updated_at)
ResourceEvent (id, resource_id, event_type, old_status, new_status, message, timestamp)
Invoice       (id, user_id, period_start, period_end, total, status[Pending|Paid], created_at)
InvoiceLine   (id, invoice_id, resource_id, flavor_id, hours, rate, amount, description)
AuditLog      (id, user_id, action, entity_type, entity_id, details_json, timestamp)
```

## Docker Compose Reference

```yaml
services:
  postgres:     # PostgreSQL 16
  api:           # .NET 10, dotnet watch, non-root pixu, Caddy: pico-api.aamar.cloud
  frontend:      # Next.js 16, npm run dev, non-root pixu, Caddy: pico.aamar.cloud
```

CORS: `pico.aamar.cloud`, `pico.ski.bd`, `localhost:3000`
PROVISIONING_MODE: `mock` (default) | `docker` | `openstack`

## TDD Cycle (every code task)

1. Write failing test
2. Run test → verify it fails for the right reason
3. Write minimal implementation
4. Run test → verify pass
5. Commit with `feat(scope): description`