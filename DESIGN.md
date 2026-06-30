# Pico — Design Notes

This document explains the **why** behind the major architectural decisions, the tradeoffs accepted, and the path forward if more time were available.

## Goals (and non-goals)

| Weight | Criterion | How this design serves it |
|--------|-----------|---------------------------|
| 20% | Product / user flow | End-to-end self-service flow: catalog → sign up → provision → monitor → pay. No dead ends. Every screen has loading/empty/error states. |
| 20% | Backend / API / data model | Clean architecture, EF Core with proper relations, RESTful API, problem details on errors. |
| 15% | Frontend | 12 pages. AA contrast, dark mode, mobile-responsive with sidebar drawer. Real SSE-driven live updates. |
| 15% | Engineering judgment | Pluggable provisioning backend. Resource state machine. Testcontainers for integration. Spec-driven dev. |
| 15% | Reliability / security / testing | Cookie auth + CSRF + RBAC. Ownership enforcement. 95 unit + 5 integration tests. PBKDF2 password hashing. |
| 10% | Docker / deployment / docs | Single-file compose. Non-root containers. Full README + DESIGN + AI_USAGE. |
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

Implemented as a static `ResourceStateMachine` class. The `Resource` entity calls `EnsureTransition` on every `TransitionTo(...)`, so invalid transitions throw at the entity boundary.

### Pluggable provisioning backend

`IProvisioningBackend` with three implementations selected at startup via `PROVISIONING_MODE`:

| Implementation | What it does |
|----------------|--------------|
| `MockProvisioningBackend` | Default. Simulates provisioning synchronously. Generates fake externalId and ipAddress. |
| `DockerProvisioningBackend` | Creates real Docker containers with CPU/RAM limits matching the selected flavor. Maps Pico image names to Docker images. |
| `OpenStackProvisioningBackend` | Real Nova API calls. Authenticates via Keystone v3, discovers compute endpoint from service catalog. Experimental. |

### Server-Sent Events for live updates

`GET /api/resources/{id}/events` is an SSE endpoint that:
1. Sends all existing events immediately (catch-up).
2. Polls the DB every 1.5s for new events.
3. Sends keep-alive comments between events.
4. Ownership is enforced — users can only stream their own resources' events.

---

## Frontend decisions

- **TanStack Query** for data fetching with proper error/loading states.
- **AuthProvider** context for cross-cutting auth state.
- **Minimalist design system**: Button (5 variants × 4 sizes via CVA), Card, Badge, Input, EmptyState, Spinner. No glass effects, no excessive animations.
- **Dark mode** via next-themes.
- **Mobile-responsive** with collapsible sidebar drawer.

---

## What I would build next (with more time)

1. **Real authentication** — OIDC via Keycloak.
2. **Postgres LISTEN/NOTIFY for SSE** — replace polling with push semantics.
3. **More resource backends** — Hetzner, DigitalOcean, AWS EC2.
4. **Real OpenStack integration** — flavor mapping, image upload, security groups.
5. **Frontend component tests** — Vitest + Testing Library.
6. **Playwright E2E smoke** — signup → provision → view → pay.
7. **CI pipeline** — GitHub Actions: build + test + Docker build.
8. **Observability** — OpenTelemetry traces, structured logs, Prometheus metrics.
9. **Per-second billing** — tiered discounts, sustained-use discounts.
10. **Webhook notifications** — fire webhooks on resource state changes.

---

## Tradeoffs accepted

- **PBKDF2 hashing** — properly salted but not Argon2id. Acceptable for a demo.
- **Polling SSE** — works, but LISTEN/NOTIFY would be better.
- **Frontend tests not implemented** — Vitest configured, tests deferred.
- **OpenStack backend is experimental** — skeleton with Keystone auth and catalog discovery, but flavor/image mapping is hardcoded.
- **Manual EF migration** — hand-written to avoid design-time tooling setup. Integration tests verify it.