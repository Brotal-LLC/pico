# Design: PICO Self-Service Cloud Module

## Approach

A clean-architecture .NET 10 backend with a pluggable provisioning engine and a Next.js 16 frontend. The provisioning engine abstracts behind `IProvisioningBackend` with three implementations: `MockProvisioningBackend` (DB-only state simulation), `DockerProvisioningBackend` (real Docker containers via Docker API), and `OpenStackProvisioningBackend` (real Nova API via OpenStack SDK). The mode is selected at startup via `PROVISIONING_MODE` env var. The frontend is a customer-facing portal with server-component reads and client-island interactivity, following Chokidar's established patterns.

## Key decisions

- **Clean architecture (4 projects)** — Domain, Application, Infrastructure, Api. Separation of concerns makes the domain model testable without infrastructure. Rejected "single project" because the assignment evaluates "maintainable structure" at 15% weight.

- **Minimal API over Controllers** — Less ceremony, equally testable, faster to iterate. Chokidar uses this pattern successfully. Rejected MVC controllers because the domain is small enough that endpoint extension methods suffice.

- **Pluggable provisioning backend** — `IProvisioningBackend` interface with three implementations. The mock is always available (zero deps for reviewers). Docker and OpenStack are opt-in via env var. This shows infrastructure understanding without making the app depend on external services. Rejected "hardcode mock only" because the hiring manager explicitly wants to see infra skills.

- **OpenStack Nova API conventions** — Flavors, images, servers, keypairs. The API surface mirrors Nova v2.1 shapes so the design note can honestly say "follows OpenStack Nova conventions." Rejected "invent our own API shape" because alignment with a real cloud API shows domain knowledge.

- **SSE for status updates** — Server-Sent Events for provisioning status transitions. Simpler than WebSockets, one-way (server→client) is all we need, works through Caddy. Rejected SignalR (overkill) and polling (poor UX).

- **EF Core 10 + PostgreSQL** — Same stack as Chokidar. Migrations for schema evolution. Rejected SQLite (reviewers might not have it, and the assignment says "PostgreSQL" explicitly in the tech freedom section).

- **Mocked cookie auth** — Simple cookie-based session with seeded users. Assignment explicitly says "Do not spend time building authentication perfectly unless it is central to your design. Simple mocked users/roles are fine." Two roles: Customer, Admin. Rejected Identity/JWT because the assignment tells us not to overbuild auth.

- **Next.js 16 + Tailwind 4 + shadcn/ui patterns** — Same as Chokidar. Server components for data-heavy pages (catalog, dashboard, invoices), client islands for interactive flows (provisioning wizard, payment). TanStack Query for client-side cache. Zod for form validation. Rejected "pure client-side SPA" because server components give better initial load and SEO for the catalog page.

- **Docker Compose single file** — One `compose.yaml` with Postgres + API + Frontend. Caddy labels for routing. Rejected separate dev/prod compose files because the assignment wants simplicity ("a reviewer should be able to clone and run").

- **Non-root containers** — All Dockerfiles create and use a non-root user (UID 1000, name `pixu`). Same pattern as Chokidar. Required by our development rules.

- **KVM VM for DevStack** — Isolated network namespace, host untouched. 16 GB / 4 vCPU / 100 GB. Rejected bare-metal DevStack because it modifies host iptables/bridges/DNS and conflicts with existing Caddy + Docker infra.

## API / interface changes

### REST API endpoints

```
POST   /api/auth/signup          → create customer account
POST   /api/auth/login            → cookie-based login
POST   /api/auth/logout           → clear session
GET    /api/auth/me               → current user info

GET    /api/catalog/flavors        → list VM flavors (CPU/RAM/disk/price)
GET    /api/catalog/flavors/{id}  → flavor detail
GET    /api/catalog/images         → list available OS images

GET    /api/resources              → list current user's resources
POST   /api/resources             → provision new resource (flavor + image + name)
GET    /api/resources/{id}        → resource detail with status
POST   /api/resources/{id}/start  → start stopped resource
POST   /api/resources/{id}/stop   → stop running resource
DELETE /api/resources/{id}        → terminate resource
GET    /api/resources/{id}/usage  → usage metering data (CPU, RAM, disk, network)
GET    /api/resources/{id}/events → SSE stream for status transitions

GET    /api/invoices               → list invoices
GET    /api/invoices/{id}          → invoice detail with line items
POST   /api/invoices/{id}/pay     → mark invoice as paid (simulated)

GET    /api/health                → service health + provisioning backend status

GET    /api/admin/users            → (admin) list all users
GET    /api/admin/resources        → (admin) list all resources across users
GET    /api/admin/metrics          → (admin) summary metrics (total users, resources, revenue)
```

### Data model

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

### Resource state machine

```
                    ┌──────────────────────────────────┐
                    ▼                                   │
  Created → Provisioning → Running ⇄ Stopped → Terminated
                ↓                ↓
            Failed          Terminated
```

Valid transitions:
- Created → Provisioning (provision starts)
- Provisioning → Running (provision succeeded)
- Provisioning → Failed (provision error)
- Running → Stopped (user stops)
- Stopped → Running (user starts)
- Running → Terminated (user terminates)
- Stopped → Terminated (user terminates)
- Failed → Terminated (user terminates failed resource)

### Provisioning backend interface

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

## Risks

- **DevStack VM instability** → The `openstack` mode is a stretch goal. If the VM breaks, the app still works in `mock` and `docker` modes. The provisioning backend is behind an interface, so fallback is instant.
- **Docker socket security** → `docker` mode requires mounting `/var/run/docker.sock`. Only used for live demo, not for reviewers. Documented clearly.
- **SSE through Caddy** → Caddy supports SSE natively, but we should verify with a quick test. Fallback: polling with 2s interval.
- **EF Core migration ordering** → Seed data must run after migrations. Use `IHostedService` or `DbContext.Database.MigrateAsync()` at startup (like Chokidar's `Database__AutoMigrate` pattern).

## Test plan

- **Unit** (`tests/Unit/`): Domain entity behavior, state machine transitions, pricing calculator, invoice generator, validation logic. Pure logic, no IO. Fast.
- **Integration** (`tests/Integration/`): API endpoint tests with Testcontainers Postgres. Provisioning backend tests (mock mode via DB, docker mode via Testcontainers Docker-in-Docker). Full lifecycle: signup → provision → stop → start → terminate → invoice → pay.
- **Regression** (`tests/Regression/`): One test per known bug found during development.
- **Manual / visual**: Screenshot diff on `<your-frontend-host>` for catalog, dashboard, resource detail, invoice, health page. Dark/light mode. Mobile breakpoint.