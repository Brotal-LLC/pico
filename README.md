# Pico — Self-Service Cloud Module

A self-service cloud platform that lets customers discover, provision, monitor, and pay for VM resources without ever opening a support ticket.

> **Assignment context:** built for FGL's Lead Full-Stack Engineer take-home test. Implements the PICO self-service cloud scenario end-to-end with a production-grade architecture.

---

## Quick Start (reviewer experience)

```bash
git clone https://github.com/Brotal-LLC/pico.git
cd pico
cp .env.example .env
# Edit .env: set POSTGRES_PASSWORD to something
docker compose up --build
```

After the build, open:
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
2. **Sign up** at `/signup` — account is created in the Customer role. Auto-logged in via cookie.
3. **Browse the catalog** at `/catalog` — see 6 VM packages with specs (vCPU, RAM, disk) and pricing.
4. **Provision a VM** — pick a package, pick an OS image, give it a name. Status transitions to `Created → Provisioning → Running` (mock mode is synchronous).
5. **Resource detail** at `/resources/{id}` — live status, usage cards, event timeline (SSE stream).
6. **Stop / Start** the resource from the detail page. State machine enforces valid transitions.
7. **Terminate** when done (with confirmation dialog). Resource moves to `Terminated` state.
8. **Invoices** at `/billing` — view monthly bills, click through to see line items, mark as paid.
9. **Admin panel** at `/admin` (admin role only) — operational metrics and user directory.
10. **Health** at `/health` — auto-refreshing service status.

---

## Architecture

**Backend** — .NET 10, ASP.NET Core, EF Core 10, PostgreSQL 16. Clean architecture (Domain / Application / Infrastructure / Api).

**Frontend** — Next.js 16, React 19, Tailwind CSS 4, TanStack Query, Zod. Client components for interactive flows.

**Provisioning** — Pluggable `IProvisioningBackend` with 3 implementations:
- **`mock`** (default): zero external deps, simulates provisioning synchronously. Used for self-contained reviewer runs.
- **`docker`**: creates real Docker containers as "VMs" via the Docker API. CPU/RAM limits match the selected flavor.
- **`openstack`**: calls Nova API to provision actual VMs. Authenticates via Keystone, discovers compute endpoint from service catalog. Experimental — requires a running OpenStack/DevStack instance.

### Project structure

```
pico/
├── src/
│   ├── Pico.Domain/           # Entities, value objects, state machine, exceptions
│   ├── Pico.Application/      # DTOs, services (Catalog, Resource, Pricing, Invoice), interfaces
│   ├── Pico.Infrastructure/   # EF Core DbContext + configs, repositories, 3 provisioning backends, seeder
│   └── Pico.Api/              # Minimal API endpoints, DI wiring, cookie auth, CORS, problem details
├── frontend/                   # Next.js 16 app (12 pages, 4 dynamic routes)
├── tests/Pico.Tests/           # xUnit: 95 unit + 5 integration (Testcontainers Postgres)
├── openspec/                   # Spec-driven development artifacts
├── compose.yaml                # Docker Compose: postgres + api + frontend
├── .env.example                # All env vars documented
├── backend/Dockerfile.{dev,prod}
├── frontend/Dockerfile.{dev,prod}
└── scripts/pre-commit.sh       # Tracked pre-commit hook (build + test + typecheck + lint)
```

### Data model (8 tables)

```
users                  — Customer/Admin accounts, password hashes (PBKDF2-HMAC-SHA256)
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

Transitions enforced by `ResourceStateMachine.CanTransition()`. Invalid transitions throw `DomainException`.

---

## Running modes

The backend's provisioning mode is selected via the `PROVISIONING_MODE` env var:

| Mode | What it does | When to use |
|------|--------------|-------------|
| `mock` (default) | Simulates state transitions in the DB. No external services. | Self-contained reviewer run, CI tests. |
| `docker` | Creates real Docker containers. Resource.externalId = container id. | Live demo of the full lifecycle. |
| `openstack` | Calls Nova API to create real VMs. | Requires a running OpenStack cluster. Experimental. |

---

## API

All endpoints are documented via OpenAPI at `/swagger` when the app is running.

**Authentication** — cookie-based session (HttpOnly, SameSite=Lax, Secure in production, 7-day sliding). CSRF protection via antiforgery tokens for state-changing requests.

**Live updates** — `GET /api/resources/{id}/events` returns Server-Sent Events. The client uses `EventSource` to subscribe and shows status transitions in real time.

---

## Testing

```bash
# All tests
dotnet test

# Just unit tests (fast, no Docker)
dotnet test --filter "FullyQualifiedName!~Integration"

# Just integration tests (requires Docker)
dotnet test --filter "FullyQualifiedName~Integration"
```

**95 unit tests** covering:
- All 8 domain entities (factory methods, invariants, state transitions)
- Resource state machine (all valid + invalid transitions)
- PricingCalculator, InvoiceGenerator
- ResourceService (lifecycle: provision, start, stop, terminate, RBAC, ownership)
- Password hasher (hash + verify)

**5 integration tests** (Testcontainers Postgres) covering:
- EF Core mappings against real Postgres
- Repository CRUD + unique constraints
- ResourceService end-to-end provisioning lifecycle

**Frontend tests** — Vitest and Playwright are configured. Component and E2E tests are a future task.

---

## Documentation

- **[`DESIGN.md`](./DESIGN.md)** — architecture decisions, tradeoffs, and what I would build next
- **[`AI_USAGE.md`](./AI_USAGE.md)** — honest reflection on how AI was used in this build

---

## Security notes

- Passwords hashed with PBKDF2-HMAC-SHA256 (100k iterations, 16-byte salt). Not Argon2id, but properly salted and slow enough for a demo. Production would use Argon2id or ASP.NET Identity.
- Cookie auth with antiforgery CSRF protection.
- Resource endpoints enforce ownership (users can only see/modify their own resources; admins can see all).
- CORS restricted to configured origins.
- Non-root Docker containers (UID 1000, user `pixu`).

---

## License

Internal — FGL engineering take-home assignment.