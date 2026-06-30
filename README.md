# Pico — Self-Service Cloud Module

A complete, working self-service cloud platform that lets customers discover, provision, monitor, and pay for VM resources without ever opening a support ticket.

> **Assignment context:** built for FGL's Lead Full-Stack Engineer take-home test. Implements the PICO self-service cloud scenario end-to-end with a production-grade architecture.

---

## Quick Start (reviewer experience)

```bash
git clone https://github.com/Brotal-LLC/pico.git
cd pico
cp .env.example .env
docker compose up --build
```

That's it. After the build, open:
- **http://localhost:3000** — customer-facing web app (marketing landing + auth + dashboard)
- **http://localhost:8080/swagger** — API documentation
- **http://localhost:8080/api/health** — service health

The app boots with the `mock` provisioning backend (zero external dependencies), auto-migrates the database, and seeds 6 VM flavors, 4 OS images, and 2 demo users.

### Demo credentials

| Email | Password | Role |
|-------|----------|------|
| `demo@pico.local` | `pico-demo-password` | Customer |
| `admin@pico.local` | `pico-admin-password` | Admin |

---

## What you can do (a 60-second tour)

1. **Land on `/`** — see the marketing page. Click *Get started* to create an account.
2. **Sign up** at `/signup` — account is created in the Customer role. Auto-logged in.
3. **Browse the catalog** at `/catalog` — see 6 VM packages with specs (vCPU, RAM, disk) and pricing.
4. **Provision a VM** — pick a package, pick an OS image, give it a name. Status transitions to `Created → Provisioning → Running` over ~5 seconds (mock mode).
5. **Resource detail** at `/resources/{id}` — live status, CPU/RAM/Network usage cards, event timeline (SSE stream).
6. **Stop / Start** the resource from the detail page. State machine enforces valid transitions.
7. **Terminate** when done. Resource moves to `Terminated` state.
8. **Invoices** at `/billing` — view monthly bills, click through to see line items, mark as paid.
9. **Admin panel** at `/admin` (admin role only) — operational metrics (users, resources, revenue) and user directory.
10. **Health** at `/health` — auto-refreshing service status.

The full user flow was deliberately kept small: **discover → sign up → provision → monitor → pay**. Every screen above is production-quality, accessible (AA contrast, 44pt+ tap targets), responsive, and dark-mode aware.

---

## Architecture

**Backend** — .NET 10, ASP.NET Core, EF Core 10, PostgreSQL 16. Clean architecture (Domain / Application / Infrastructure / Api).

**Frontend** — Next.js 16, React 19, Tailwind CSS 4, shadcn/ui patterns, TanStack Query, Zod, Recharts. Server components for catalog reads, client islands for interactive flows.

**Provisioning** — Pluggable `IProvisioningBackend` with 3 implementations:
- **`mock`** (default): zero external deps, simulates state transitions with 2-5s delay. Used for self-contained reviewer runs.
- **`docker`**: creates real Docker containers as "VMs" via the Docker API. Mounts `/var/run/docker.sock`.
- **`openstack`**: calls Nova API to provision actual VMs. Configured to point at a DevStack installation.

**Stack consistency** — same as our other internal services. If you've used our other repos, this looks familiar.

### Project structure

```
pico/
├── src/
│   ├── Pico.Domain/           # Entities, value objects, state machine, exceptions
│   ├── Pico.Application/      # DTOs, services (Catalog, Resource, Pricing, Invoice), interfaces
│   ├── Pico.Infrastructure/  # EF Core DbContext + configs, repositories, 3 provisioning backends, seeder
│   └── Pico.Api/              # Minimal API endpoints, DI wiring, cookie auth, CORS, problem details
├── frontend/                   # Next.js 16 app (12 pages, 4 dynamic routes)
├── tests/Pico.Tests/           # xUnit: 91 unit + 5 integration (Testcontainers Postgres)
├── openspec/                   # Spec-driven development artifacts
├── compose.yaml                # Single-file Docker Compose: postgres + api + frontend + Caddy labels
├── .env.example                # All env vars documented
├── backend/Dockerfile.dev      # Non-root dev image (pixu user, hot reload)
├── backend/Dockerfile.prod     # Multi-stage production image
└── frontend/Dockerfile.{dev,prod}
```

### Data model (8 tables)

```
users                  — Customer/Admin accounts, password hashes (SHA256 over dev-only)
flavors                — VM packages (vcpus, ram_mb, disk_gb, price_per_hour, price_per_month, category)
images                 — OS images (Ubuntu, Debian, AlmaLinux)
resources              — Provisioned VMs (status, external_id, ip_address, current state)
resource_events        — Append-only state-transition log (drives the SSE feed)
invoices               — Monthly bills per customer
invoice_lines          — Per-resource usage lines on each invoice
audit_logs             — Security/audit trail (JSONB details)
```

### State machine

```
Created ──▶ Provisioning ──▶ Running ⇄ Stopped ──▶ Terminated
                  └─▶ Failed ─────────────────────▶ Terminated
```

Transitions enforced by `ResourceStateMachine.CanTransition()`. Invalid transitions throw `DomainException`. Tested with 18+ unit tests covering all valid/invalid edges.

---

## Running modes

The backend's provisioning mode is selected via the `PROVISIONING_MODE` env var:

| Mode | What it does | When to use |
|------|--------------|-------------|
| `mock` (default) | Simulates state transitions in the DB. No external services. | Self-contained reviewer run, CI tests. |
| `docker` | Creates real Docker containers. Resource.externalId = container id, ip_address from container network. | Live demo of the full lifecycle against a real substrate. |
| `openstack` | Calls Nova API to create real VMs. | Live demo against a real OpenStack cluster (DevStack, Mirantis, etc.). |

To run in `docker` mode:
```bash
# In .env, set:
PROVISIONING_MODE=docker
# And expose /var/run/docker.sock to the API container (compose.yaml mounts it conditionally)
```

To run in `openstack` mode, set `PROVISIONING_MODE=openstack` and configure the `OpenStack:*` connection vars in `.env`.

---

## API

All endpoints are documented via OpenAPI at `/swagger` when the app is running. The surface:

```
POST   /api/auth/signup
POST   /api/auth/login
POST   /api/auth/logout
GET    /api/auth/me

GET    /api/catalog/flavors
GET    /api/catalog/flavors/{id}
GET    /api/catalog/images

GET    /api/resources
POST   /api/resources              (provision)
GET    /api/resources/{id}
POST   /api/resources/{id}/start
POST   /api/resources/{id}/stop
DELETE /api/resources/{id}
GET    /api/resources/{id}/usage
GET    /api/resources/{id}/events  (Server-Sent Events)

GET    /api/invoices
GET    /api/invoices/{id}
POST   /api/invoices/{id}/pay

GET    /api/admin/metrics      (admin role)
GET    /api/admin/users
GET    /api/admin/resources

GET    /api/health
```

**Authentication** — cookie-based session (HttpOnly, SameSite=Lax, Secure in production, 7-day sliding). The auth middleware sets a `Pico.Auth` cookie. RBAC enforced via `RequireAuthorization()` + admin endpoint filter.

**Live updates** — `GET /api/resources/{id}/events` returns Server-Sent Events. The client uses `EventSource` to subscribe and shows status transitions in real time, with polling fallback if SSE fails.

---

## Testing

```bash
# All tests
dotnet test

# Just unit tests (fast, no Docker)
dotnet test --filter "FullyQualifiedName!~Integration"

# Just integration tests (Testcontainers Postgres)
dotnet test --filter "FullyQualifiedName~Integration"
```

**91 unit tests** covering:
- All 8 domain entities (factory methods, invariants, state transitions)
- Resource state machine (all 12 valid transitions + every invalid one)
- PricingCalculator (hourly, monthly, per-day, edge cases)
- InvoiceGenerator (multi-resource sums, zero-hour skips)
- ResourceService (lifecycle: provision, start, stop, terminate, RBAC)
- Auth, catalog, repository contracts (with in-memory fakes)

**5 integration tests** covering:
- EF Core mappings against real Postgres
- UserRepository CRUD + unique email constraint
- FlavorRepository list ordering
- ResourceRepository event append
- ResourceService end-to-end provisioning lifecycle

**Frontend tests** (via Vitest) are scaffolded but not implemented in this version — the API was the priority. Add them under `frontend/src/**/__tests__/`.

---

## Documentation

- **[`DESIGN.md`](./DESIGN.md)** — architecture decisions, tradeoffs, and what I would build next with more time
- **[`AI_USAGE.md`](./AI_USAGE.md)** — honest reflection on how AI was used in this build

---

## License

Internal — FGL engineering take-home assignment.
