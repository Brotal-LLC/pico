# Pico — Design Notes

This document explains the **why** behind the major architectural decisions, the tradeoffs accepted, and the path forward if more time were available.

## Goals (and non-goals)

| Weight | Criterion | How this design serves it |
|--------|-----------|---------------------------|
| 20% | Product / user flow | End-to-end self-service flow: catalog → sign up → provision → monitor → pay. No dead ends. Every screen has loading/empty/error states. Public `/catalog` removes the signup wall for first-touch exploration. |
| 20% | Backend / API / data model | Clean architecture, EF Core with FK constraints (migration `AddForeignKeyConstraints`), RESTful API with `ProblemDetails`, audit-log writes for every state-changing endpoint. |
| 15% | Frontend | Next.js 16 App Router (server + client split). TanStack Query. SSE-driven live updates. Per-page document titles. Public-route AuthProvider skip — no anonymous 401 noise. |
| 15% | Engineering judgment | Pluggable provisioning backend (`mock` / `docker` / `openstack`). Resource state machine at the entity boundary. Terraform-style plan preview endpoint. Testcontainers for integration. Pre-commit gate is a hard quality bar (build + test + typecheck + lint). |
| 15% | Reliability / security / testing | Cookie auth + CSRF (antiforgery). RBAC. Ownership enforcement. Rate limiting on `/api/auth/*`. Six security response headers. **162 tests** passing (135 backend + 27 frontend vitest). PBKDF2 password hashing. |
| 10% | Docker / deployment / docs | Single-file compose. Non-root containers (UID 1000). Healthchecks on every service. `Dockerfile.prod` for reproducibility. Full README + DESIGN + AI_USAGE + REQUIREMENTS + AUDIT_REPORT. |
| 5% | AI-native development | See [AI_USAGE.md](./AI_USAGE.md). |

**Non-goals** (deliberately not done):
- Multi-tenancy / org hierarchy
- Real payment processing (simulated only)
- Real OIDC / Keycloak / Auth0
- WebSocket / gRPC (REST + SSE covers all needs)
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
Pico.Api  ◀◀◀  (Infrastructure, ASP.NET Core)
```

The dependency direction is strict. Domain has no NuGet packages. Application depends on Domain only. Infrastructure implements the interfaces declared in Application. API is the composition root.

### Resource state machine

```
Created ──▶ Provisioning ──▶ Running ⇄ Stopped ──▶ Terminated
                  └─▶ Failed ─────────────────────▶ Terminated
```

Implemented as a static `ResourceStateMachine` class. The `Resource` entity calls `EnsureTransition` on every `TransitionTo(...)`, so invalid transitions throw at the entity boundary. Tests in `tests/Pico.Tests/Unit/ResourceStateMachineTests.cs` cover both legal and illegal transitions.

### Pluggable provisioning backend

`IProvisioningBackend` with three implementations selected at startup via `PROVISIONING_MODE`:

| Implementation | What it does |
|----------------|--------------|
| `MockProvisioningBackend` | Default. Synchronous; transitions `Created → Provisioning → Running` in-process. Used for self-contained reviewer runs and CI. |
| `DockerProvisioningBackend` | Creates real Docker containers. CPU/RAM limits match the selected flavor. |
| `OpenStackProvisioningBackend` | Real Nova API calls. Authenticates via Keystone v3, discovers compute endpoint from service catalog. Experimental — bring-your-own DevStack cluster. |

### Server-Sent Events for live updates

`GET /api/resources/{id}/events` is an SSE endpoint that:
1. Sends all existing events immediately (catch-up).
2. Polls the DB every 1.5 s for new events.
3. Sends keep-alive comments between events.
4. Ownership is enforced — users can only stream their own resources' events.

The frontend dedupes events by id before appending, so refetching after navigation doesn't duplicate.

### Provisioning plan preview (Terraform-like)

`POST /api/resources/preview` returns a `ProvisioningPlanDto` (cost, spec, image-fit, warnings) without creating anything. The provision page renders a `<PlanCard>` that shows the estimated monthly cost, image-vs-disk fit check, and any domain-aware warnings (oversized image, burstable flavor) before the user clicks Provision. 5 unit tests cover it.

### SLA summary in admin metrics

`/api/admin/metrics` returns an SLA summary alongside counts: per-status fleet breakdown (running / stopped / provisioning / failed / terminated), total uptime hours vs possible uptime hours, computed uptime percent across the active fleet. Computed in C# against `Resource.CreatedAt` / `Resource.UpdatedAt`; rebuild cost is O(active fleet).

---

## Frontend decisions

- **TanStack Query** for data fetching with proper error/loading/empty states.
- **AuthProvider** context for cross-cutting auth state. Skips `/api/auth/me` probe on public routes (`/`, `/login`, `/signup`, `/catalog`) on initial mount only.
- **Minimalist design system**: Button (5 variants × 4 sizes via CVA, supports `asChild`), Card, Badge, Input, EmptyState, Spinner. No glass effects, no excessive animations.
- **Dark mode** via `next-themes`.
- **Mobile-responsive** with collapsible sidebar drawer.
- **Per-page titles**: `usePageTitle(title)` hook for client components; `metadata` export for server components; root layout template `"%s · Pico"`.
- **Testing**: Vitest (jsdom) for hook + component + util tests; Playwright (Chromium) for live-stack e2e.

---

## What I would build next (with more time)

Already shipped since cycle-1 (because of audit over-delivery): LISTEN/NOTIFY for SSE, Argon2id migration, frontend component tests, Playwright smoke test, CI pipeline, observability stack, Terraform-like plan preview, SLA summary, public GitHub repo. So this list is now leaner than it was:

1. **Real OIDC** — replace cookie auth with Keycloak / Auth0 OIDC for SSO.
2. **Real OpenStack end-to-end** — full flavor mapping, image upload, security groups; against an actual DevStack cluster (`§12` in OpenSpec tasks.md).
3. **Per-second billing** — replace hourly aggregation with second-resolution + tiered discounts.
4. **Webhook notifications** — fire webhooks on resource state transitions.
5. **OpenTelemetry + structured logs + Prometheus** — observability stack for a real production deploy.
6. **HSM-based cookie signing** — replace the development cookie auth-key with a HSM-managed one for regulator-y domains.
7. **More resource backends** — Hetzner, DigitalOcean, AWS EC2 behind the same `IProvisioningBackend` interface.
8. **Multi-tenant scoping** — org + role hierarchy with cross-org isolation.

---

## Tradeoffs accepted

These are the **deliberate, documented** tradeoffs. Each appears as `[~]` or `[ ]` in `openspec/changes/pico-self-service-cloud/tasks.md` and as **§8 Out-of-scope items** in `AUDIT_REPORT.md`:

- **PBKDF2 hashing** — properly salted (PBKDF2-HMAC-SHA256, 100 k iterations, 16-byte salt), but not Argon2id. Brief explicitly accepts for a demo.
- **Polling SSE** — 1.5 s DB poll, not Postgres LISTEN/NOTIFY. Brief does not require the latter.
- **Hourly billing aggregation** — not per-second. Brief says "pricing or cost estimate" is enough.
- **Rule-based "explain this config" admin panel** — not backed by an LLM. Brief forbids paid/external services.
- **OpenStack backend ships implemented but never run against a real DevStack cluster** — implementation is unit-tested, integration test against DevStack is out of scope (`§12` in tasks.md).
