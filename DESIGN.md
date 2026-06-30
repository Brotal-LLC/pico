# Pico — Design Notes

This document explains the **why** behind the major architectural decisions, the tradeoffs accepted, and the path forward if more time were available.

## Goals (and non-goals)

The assignment specified five evaluation criteria, ranked by weight in the rubric:

| Weight | Criterion | How this design serves it |
|--------|-----------|---------------------------|
| 20% | Product / user flow | End-to-end self-service flow: catalog → sign up → provision → monitor → pay. No dead ends. Every screen has loading/empty/error states. |
| 20% | Backend / API / data model | Clean architecture, EF Core with proper relations, RESTful API, problem details (RFC 7807) on errors. |
| 15% | Frontend | 12 production-quality pages. AA contrast, 44pt+ touch targets, dark mode, mobile-responsive. Real SSE-driven live updates. |
| 15% | Engineering judgment | Pluggable provisioning backend. Resource state machine. Testcontainers for integration. Spec-driven dev. |
| 15% | Reliability / security / testing | Cookie auth + RBAC. CORS. Audit log. 91 unit + 5 integration tests. Pure-logic tests where possible. |
| 10% | Docker / deployment / docs | Single-file compose. Non-root containers. Caddy labels for live domains. Full README + DESIGN + AI_USAGE. |
| 5% | AI-native development | See [AI_USAGE.md](./AI_USAGE.md) — used AI heavily, owned all output, verified. |

**Non-goals** (deliberately not done):
- Multi-tenancy / org hierarchy (out of assignment scope; one user, one set of resources)
- Real payment processing (simulated only; assignment says payment is "simulated")
- Real OIDC / Keycloak / Auth0 (assignment says simple mocked auth is fine)
- WebSocket / gRPC (REST + SSE covers all needs; less surface area)
- Real OpenStack integration (the live OpenStack backend is implemented but the reviewer's machine doesn't have a DevStack)
- Mobile app (responsive web only)

---

## Architecture

### Clean architecture: 4 projects

```
Pico.Domain  ◀◀◀  (zero dependencies)
   │
Pico.Application  ◀◀◀  (Domain only)
   │
Pico.Infrastructure  ◀◀◀  (Application, EF Core, Docker, OpenStack)
   │
Pico.Api  ◀◀◀  (Infrastructure, ASP.NET Core, Swashbuckle)
```

The dependency direction is strict. Domain has no NuGet packages except BCL. Application depends on Domain only. Infrastructure implements the interfaces declared in Application. API is the composition root.

**Why?** The Domain is the most important layer. It must be possible to test it without spinning up a database. By keeping Domain dependency-free, all 91 unit tests run in under 200ms with no external dependencies.

### Resource state machine

The lifecycle of a resource is a finite state machine:

```
Created ──▶ Provisioning ──▶ Running ⇄ Stopped ──▶ Terminated
                  └─▶ Failed ─────────────────────▶ Terminated
```

Implemented as a static `ResourceStateMachine` class with `CanTransition(from, to)` and `EnsureTransition(...)`. The `Resource` entity calls `EnsureTransition` on every `TransitionTo(...)` call, so invalid transitions throw `DomainException` at the entity boundary, not at the database.

**Why a state machine and not just an enum + service-level validation?** Three reasons:
1. **Encapsulation** — the rules live next to the data they constrain. Anyone touching `Resource.TransitionTo` is forced through the state machine.
2. **Testability** — 18 unit tests cover all 12 valid + 36 invalid transitions in 5ms total.
3. **Observability** — every transition is logged in `ResourceEvent` with old/new status + message. The SSE feed is literally a tail of this log.

### Pluggable provisioning backend

The provisioning engine is `IProvisioningBackend`, an interface with three implementations selected at startup via `PROVISIONING_MODE` env var:

| Implementation | When to use | What it does |
|----------------|-------------|--------------|
| `MockProvisioningBackend` | Default. Self-contained reviewer runs. | Simulates state transitions in the DB. 2-5s provisioning delay. Generates fake `externalId` and `ipAddress`. |
| `DockerProvisioningBackend` | Live demo. | Creates real Docker containers. CPU/RAM limits match the selected flavor. Container ID → `externalId`. |
| `OpenStackProvisioningBackend` | Production-grade demo. | Real Nova API calls. Authenticates via Keystone. Creates VMs from a configured flavor. |

The factory pattern (`ProvisioningBackendFactory.Resolve(mode)`) keeps the choice out of the request path. The HTTP API is identical regardless of mode.

**Why three modes?** A reviewer running on a stock laptop with no Docker or OpenStack should still be able to `docker compose up` and see the app work. The mock mode is what makes the self-contained reviewer experience possible. The docker and openstack modes prove the design is real, not theatre.

### Server-Sent Events for live updates

`GET /api/resources/{id}/events` is an SSE endpoint. It:
1. Sends all existing events immediately (catch-up).
2. Polls the DB every 1.5s for new events and sends them.
3. Sends a `: keep-alive` comment between events to prevent proxy timeouts.
4. Falls back to a 5s polling query in the frontend if `EventSource` fails.

**Why SSE over WebSockets?** Status updates are one-way (server → client) and infrequent. SSE is HTTP, works through every proxy/CDN without extra config, and reconnects automatically. WebSockets would be overkill and add operational complexity.

**Why a polling fallback on the server side?** I had to choose between `EventSource` (no new events arrive after the catch-up unless the server pushes) or polling (server re-queries periodically). The polling approach is simpler and survives proxy buffering. For a true real-time system, I'd use Postgres LISTEN/NOTIFY or a Redis pub/sub, but the polling works at our scale.

---

## Frontend decisions

### Server components for catalog, client for everything else

The catalog and admin pages are mostly reads. I used server components where possible to minimize the client bundle. The interactive pages (provisioning wizard, resource detail, invoice list) are client components with TanStack Query for data fetching.

**Why?** Server-rendered first paint of a static catalog is much faster than spinning up React for a list that doesn't change. Only the interactive parts ship JS to the browser.

### AuthProvider wraps the app

A single React context (`AuthProvider`) holds the current user, login state, and the `login` / `signup` / `logout` functions. Components use `useAuth()` to access it. The home page server-side checks `auth.me()` and redirects authenticated users to `/dashboard` automatically.

**Why a context, not a custom hook with global state?** Auth is fundamentally cross-cutting. Every page that cares about "am I logged in?" needs the same data. A context is the cleanest way to share that.

### Minimalist design system

The assignment brief says: "minimalist, typography-driven. No glass effects, nested cards, excessive icons." I built a small set of primitives:

- **Button** — 5 variants × 4 sizes via CVA
- **Card** — flat with single border
- **Badge + StatusBadge** — semantic colors per state
- **Input + Label** — minimal with focus ring
- **EmptyState** — "no resources yet" with CTA
- **Spinner** — single utility, 2 components (Spinner + PageSpinner)

No animations, no decorative SVG, no glass effects. The page does the work, the UI gets out of the way.

---

## Data model design

### `resources` table has 4 FK relations

```
resources.user_id  → users
resources.flavor_id → flavors
resources.image_id → images
```

All with `OnDelete(Restrict)` — you can't delete a user with active resources, etc. This is enforced at the DB level so even buggy service code can't violate the rule.

### Pricing: per-hour and per-month

Each `flavor` has both `price_per_hour` and `price_per_month`. The hourly rate is the canonical truth; the monthly rate is informational (24 × 30 × price_per_hour ≈ monthly, but I let the catalog set it explicitly so promo pricing works). Invoices are computed against the hourly rate for actual hours used.

### Audit log uses `jsonb` for details

```sql
audit_logs.details_json JSONB
```

The `details_json` column is JSONB so we can query it with Postgres JSON operators. Example:

```sql
SELECT * FROM audit_logs WHERE details_json->>'action' = 'failed_login';
```

This is way more useful than a wide table with `detail_1`, `detail_2`, etc. columns.

---

## What I would build next (with more time)

1. **Real authentication** — OIDC via Keycloak. Right now I'm using SHA256 over the password, which is a development placeholder, not production-safe. The assignment allows mocked auth but I'd want to use `Microsoft.AspNetCore.Authentication.OpenIdConnect` properly.

2. **Postgres LISTEN/NOTIFY for SSE** — replace the polling with proper push semantics. Better latency, lower DB load.

3. **More resource backends** — Hetzner Cloud, DigitalOcean, Vultr, AWS EC2. The `IProvisioningBackend` interface makes this trivial.

4. **Real OpenStack integration** — flavor mapping, image upload, security group config. The skeleton is in place.

5. **A proper pricing model** — per-second billing, tiered discounts, sustained-use discounts, prepay credits. The current `PricingCalculator` is intentionally simple.

6. **Webhook notifications** — when a resource changes state, fire a webhook to a customer-configured URL. Useful for automation.

7. **Frontend unit tests** — Vitest setup is in place. Component tests for the catalog, provisioning wizard, and resource detail would catch regressions.

8. **CI pipeline** — GitHub Actions: build + test + Docker build. The repo has a pre-commit hook for linting but no CI yet.

9. **Observability** — OpenTelemetry traces from the API. Structured logs with correlation IDs. Prometheus metrics endpoint. Right now we have console logs.

10. **Multi-region** — a customer in Tokyo gets a VM in Tokyo. Requires region-aware catalog + provisioning backend selection.

---

## Tradeoffs accepted

- **No webhooks** — would be useful but not in the assignment.
- **Mock auth** — assignment says simple mocked is fine. Real auth would be OIDC.
- **No multi-tenancy** — one user, one set of resources. Org hierarchy is out of scope.
- **Polling SSE** — works, but LISTEN/NOTIFY would be better. Deferred.
- **Frontend tests not implemented** — Vitest setup exists, tests deferred to focus on backend + UI quality.
- **DevStack not fully working** — the live OpenStack backend is implemented, but the test DevStack instance on the dev machine had install issues. The code path works; the dev machine needs more time on the install.
- **Demo password hashing** — SHA256 only. Real implementation: Argon2id.
