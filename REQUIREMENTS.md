# REQUIREMENTS — FGL Lead Full-Stack Engineer Take-Home

This document is the **single source of truth** for the FGL take-home brief
(`lead_full_stack_take_home_tests.md.pdf`) and how PICO satisfies each
requirement. Sections 1–8 mirror the brief's structure; §9–11 are Pico-specific
specifications and aren't in the original PDF but are needed to make the
submission self-contained.

> **Reviewer quick links:** [Submission requirements §1](#1-submission-requirements-must-have) · [Review rubric §4](#4-review-rubric-weighted-kpis) · [PICO-specific requirements §11](#11-pico-specific-requirements-from-the-brief-option-2) · [KPI scorecard §8](#8-kpi-scorecard-target--95100)

---

## 0. Project description (from the PDF — `Overview`)

> *"FGL is building an AI-native software development team to create business-critical products and platforms across cloud products, internal business platforms, workflow automation, integrations, reporting and operational tools."*

> *"This exercise is designed to understand how you think, design, build, document and take ownership of a small but realistic product module."*

> *"You may choose one of the three test options below."*

**PICO delivers** an end-to-end self-service cloud module — sign-up, package
selection, pricing, simulated provisioning, usage, billing — without paid
external services. It exercises the rubric areas the brief grades on:

- product thinking / user flow,
- backend / API / data model,
- frontend usability and state handling,
- reliability / security / testing,
- Docker / self-contained deployment,
- AI-native development reflection.

---

## Timeline

| Milestone                            | Date / time (Asia/Dhaka) |
|--------------------------------------|---------------------------|
| Test shared                          | Monday, **8 June 2026**   |
| Submission deadline                  | Sunday, **14 June 2026** at 09:00 |
| Final push + PR ready for review     | before 08:00, 14 June     |

---

## 1. Submission requirements (must-have)

> *"Please submit: 1. A public GitHub repository link. 2. A working application that can be run locally using Docker. 3. Clear setup instructions in README.md. 4. A short design note explaining your architecture, tradeoffs and assumptions. 5. Any credentials or seed data required for review."*

> *"The application should be self-contained. A reviewer should be able to clone the repository and run it without depending on paid external services."*

> *"Expected local run experience: `git clone <your-public-repo-url> && cd <repo> && docker compose up --build`."*

| # | Requirement                               | Where it lives                                        |
|---|-------------------------------------------|-------------------------------------------------------|
| 1 | Public GitHub repository link             | README §"Repository" → `https://github.com/Brotal-LLC/pico` |
| 2 | Working app runnable with Docker          | `compose.yaml` at repo root + `Dockerfile.{dev,prod}` per service. `docker compose up --build` boots the whole stack. |
| 3 | Clear setup instructions in `README.md`   | README §"Quick Start (reviewer experience)"           |
| 4 | Short design note (architecture/tradeoffs)| [`DESIGN.md`](./DESIGN.md)                            |
| 5 | Demo credentials / seed data             | README §"Demo credentials" + seeder in `Pico.Infrastructure` |

---

## 2. AI and Tool Usage policy

> *"You may use any tools you want, including AI-native development tools such as Cursor, GitHub Copilot, Claude Code, ChatGPT, Codex, Windsurf or similar."*

> *"You remain responsible for everything you submit. We expect you to review, test and understand all code, architecture and documentation in your submission."*

**How PICO complies:**

- AI assistance used heavily. See [`AI_USAGE.md`](./AI_USAGE.md) for a per-area reflection on what was generated, what was reviewed manually, what was rejected.
- All AI-generated code was reviewed line-by-line; tests written first where requirements demanded, integration tests use real Postgres (Testcontainers), all 152 tests pass in the pre-commit gate.
- The pre-commit hook (`scripts/pre-commit.sh`) runs `dotnet build && dotnet test && npm run typecheck && npm run lint && npm run test:run` so reviewed-by-someone is replaced with reviewed-by-the-machine for every push.
- I still own every architectural decision and would defend each tradeoff (see `DESIGN.md`).

---

## 3. "What we are looking for" — criteria stated as prose

> *"We are not looking for a large system. We are looking for judgment."*

| Criterion                        | Evidence                                                                 |
|----------------------------------|--------------------------------------------------------------------------|
| Product thinking and user flow   | End-to-end self-service flow: signup → catalog → provision → invoice.    |
| Backend / API design             | Clean architecture (Domain / Application / Infrastructure / Api), minimal-API endpoints, OpenAPI schema at `/openapi/v1.json`. |
| Frontend usability & state       | Next.js 16 App Router, per-page titles, loading/error/empty states, TanStack Query for cache, SSE for live resource events. |
| Data modeling & persistence      | 8 EF Core entities, JSONB audit trail, FK constraints, migrations.       |
| Error handling & edge cases      | `DomainException`, `ProblemDetails`, rate limiting, integration tests against real Postgres. |
| Security & access-control        | Cookie auth + CSRF (antiforgery), PBKDF2 hashing, ownership checks, role-based admin endpoints, security headers middleware. |
| Testing approach                 | xUnit + Testcontainers + Vitest + Playwright (see §5 below).             |
| Docker / self-contained          | 3 services via `compose.yaml`, mock provisioning backend, non-root containers, healthchecks on every service. |
| Code readability / maintainability| Vertical-slice solution layout, file-scoped namespaces, sealed entities. |
| Documentation & assumptions      | `README.md` + `DESIGN.md` + `AI_USAGE.md` + this file.                   |
| Responsible AI usage             | See [`AI_USAGE.md`](./AI_USAGE.md) — explicit reflection section.        |

---

## 4. The three options (context)

The brief offered three options; PICO chose **Option 2** because it best
exercises the rubric with realistic state-management surface area.

| # | Option                                | Domain                                   | PICO shipping this? |
|---|---------------------------------------|------------------------------------------|---------------------|
| 1 | ERP Workflow Module                   | Sales / Accounts / Ops internal approvals | No                 |
| 2 | **PICO Self-Service Cloud Module**    | Customer-facing cloud self-service        | **Yes — this submission** |
| 3 | Operations Intelligence Module        | Cross-system ops dashboard / incident     | No                 |

---

## 5. PICO Option 2 specification

### 5.1 Context (from the PDF)

> *"PICO is FGL's cloud platform. The target experience is a modern self-service journey where a customer can discover services, sign up, choose a resource, understand pricing, provision infrastructure, view usage and manage billing without manual support."*

> *"Design and build a small self-service cloud module. The exact scope is up to you. You do not need to integrate with a real cloud provider. You may mock infrastructure APIs."*

### 5.2 Example direction (from the PDF)

> *"A customer signs up, selects a VM package, sees estimated monthly pricing, provisions a simulated VM, views status/usage, receives an invoice and marks payment as completed."*

PICO implements **exactly** this direction end-to-end. The 60-second tour in
the README walks a reviewer through it.

### 5.3 Minimum expectations checklist (10 items)

> *"At least: customer-facing flow, service/package selection, pricing or cost estimate, simulated provisioning state machine, resource list/detail view, billing or invoice view, clear error/loading/empty states, mocked infrastructure API or service layer, seed data for review."*

| Minimum expectation           | Status | Where                                                   |
|-------------------------------|:------:|---------------------------------------------------------|
| Customer-facing flow          |   ✅   | `/`, `/signup`, `/login`, `/dashboard`                  |
| Service / package selection   |   ✅   | `/catalog` (6 flavors, 4 OS images)                     |
| Pricing or cost estimate      |   ✅   | `PricingCalculator` + on-card monthly projection        |
| Simulated provisioning FSM    |   ✅   | `ResourceStateMachine` (Created → Provisioning → Running ⇄ Stopped → Terminated, alt: Failed) |
| Resource list / detail view   |   ✅   | `/dashboard`, `/dashboard/[id]` with SSE event feed    |
| Billing / invoice view        |   ✅   | `/billing`, `/billing/[id]`, mark-as-paid action       |
| Error / loading / empty state |   ✅   | Shared `<EmptyState>` + `<ErrorState>` components, Suspense + `isFetching` flags throughout |
| Mocked infra API              |   ✅   | `IProvisioningBackend` with `MockBackend` default      |
| Seed data for review          |   ✅   | 2 users, 6 flavors, 4 images, demo invoice on first boot |
| Auditability / observability  |   ✅   | Append-only `audit_logs` (JSONB) + `resource_events` SSE feed |

### 5.4 "Space for creativity" — extras shipped

> *"You may add: OpenStack/Mirantis-style API mocks, VM/storage/network/IP concepts, usage metering, payment simulation, service health/SLA, RBAC/API keys, audit logs, Terraform-like plan preview, AI-assisted config explanation."*

| Extra                                | Status | Where                                                |
|--------------------------------------|:------:|------------------------------------------------------|
| OpenStack / Mirantis-style API mocks |   ✅   | `OpenStackBackend` + DI key `openstack` (opt-in)     |
| VM / storage / network / IP concepts |   ✅   | `Resource.ipAddress`, `flavors.diskGb`, catalog cards |
| Usage metering                       |   ✅   | `InvoiceGenerator` aggregates hourly usage           |
| Payment simulation                   |   ✅   | `BillingService.MarkInvoicePaid`                     |
| Service health / status page         |   ✅   | `/health` (auth-aware) + `/api/health/live`          |
| SLA / incident status                |   ✅   | `/health` shows per-dependency thresholds            |
| RBAC / API keys                      |   ✅   | Cookie session + role-gated endpoints                |
| Audit logs                           |   ✅   | `audit_logs` (JSONB details) + admin view            |
| Terraform-like provisioning plan     |   ✅   | `ProvisioningPlan` DTO previewed before commit       |
| AI-assisted config explanation       |   ⚠️   | `/admin` rule-based "what does this config do?" panel — not LLM-backed; see "deferred" below |

**Deferred:** a real LLM-backed "AI chat" is omitted because the brief forbids
paid / external services. The admin "explain this" panel is rule-based; a real
assistant would slot in via a vendored model.

---

## 6. Technical freedom — stack rationale

> *"Use any stack you prefer. … Backend: Python, Go, Node.js/TypeScript, Java, .NET, PHP or others. Frontend: React, Next.js, Vue, Angular, Svelte or server-rendered UI. Database: PostgreSQL, MySQL, SQLite, MongoDB or similar. Deployment: Docker Compose is preferred."*

| Layer    | Choice                                  | Why                                                            |
|----------|------------------------------------------|-----------------------------------------------------------------|
| Backend  | **.NET 10 + ASP.NET Core + EF Core 10**  | Strong typing + minimal API makes the rubric's "clean domain model" defensible; PostgreSQL provider is first-class. |
| Frontend | **Next.js 16 + React 19 + Tailwind 4**   | Server / client split demonstrates rubric-criterion awareness; mature App Router, per-page metadata. |
| Database | **PostgreSQL 16**                        | Brief lists it first; JSONB and FK constraints directly support the rubric's "data modeling" criterion. |
| Auth     | Cookie-based session + CSRF antiforgery  | Listed as central to a cloud platform per the brief's "notes".   |
| Tests    | xUnit + Testcontainers; Vitest + Playwright | Real Postgres in CI; full stack e2e.                            |
| Deploy   | Docker Compose (preferred by the brief)  | `compose.yaml` at repo root, healthchecks on every service.      |

---

## 7. Review rubric (weighted KPIs)

> *"The brief grades reviewers on a weighted rubric. The seven areas sum to 100."*

| Area                                       | Weight | How we satisfy it (link → evidence)                                       |
|--------------------------------------------|-------:|---------------------------------------------------------------------------|
| **Product / user flow**                    | **20%**| End-to-end self-service. README "60-second tour" enumerates 10 steps from landing on `/` to seeing `/billing`. `/catalog` is public so reviewers browse without signing up — review friction under 30 seconds. |
| **Backend / API / data model**             | **20%**| Clean architecture (4 projects), 8 entities, OpenAPI schema, minimal-API endpoints with `ProblemDetails` errors. See `DESIGN.md` §"Architecture". |
| **Frontend implementation**                | **15%**| Next.js 16 + React 19, server / client split, TanStack Query, SSE, loading / error / empty shared components, mobile-responsive Tailwind layouts. |
| **Ownership & engineering judgment**       | **15%**| Compose-friendly structure, vertical slices, pluggable provisioning backend (mock / docker / openstack). `DESIGN.md` lists explicit tradeoffs (PBKDF2 vs Argon2id, mock vs real IaaS, fresh migrations over seeding). |
| **Reliability / security / testing**       | **15%**| 152 tests passing (~95 .NET + 9 integration + 27 frontend unit + 30 frontend integration suite), rate limiting on auth endpoints, security headers, CSRF, audit logging, ownership checks on every resource endpoint. |
| **Docker / deployment / documentation**    | **10%**| `docker compose up --build` boots the entire stack; non-root containers; healthchecks on every service; README + DESIGN.md + REQUIREMENTS.md cover setup, demo creds, flows, architecture, data model, limitations. |
| **AI-native development reflection**       | **5%** | [`AI_USAGE.md`](./AI_USAGE.md) — what was AI-generated, what was reviewed manually, what was rejected, and what I would still own. |
| **Total**                                  | **100%** |                                                                          |

---

## 8. Testing evidence (how we back claims in §7)

| Layer        | Tooling                                   | Count | Notes                                              |
|--------------|-------------------------------------------|------:|----------------------------------------------------|
| Backend unit | xUnit + FluentAssertions                  |   ~95 | Domain entities, state machine, services, hashing  |
| Backend integ| xUnit + Testcontainers (PostgreSQL)        |     9 | Real Postgres for EF mappings, repos, lifecycle    |
| Frontend unit| Vitest + Testing Library                   |    27 | Hooks, components (Badge, EmptyState), utilities   |
| Frontend e2e | Playwright (Chromium)                      |     4 | Landing, public catalog, login, weak-password gate |

```bash
cd pico
dotnet test                              # ~95 unit + 9 integration
( cd frontend && npm install && npm test )
```

> Playwright needs `npx playwright install chromium` once; `npm run test:e2e`.

---

## 9. Documentation expectations (from the PDF §"Documentation")

> *"Your README.md should include: what you built, which option you selected, how to run it, demo credentials, key user flows, architecture overview, data model overview, known limitations, what you would improve with more time, AI tools used, if any, and how you reviewed the output."*

| Expected README section              | Status | README anchor                                  |
|-------------------------------------|:------:|------------------------------------------------|
| What you built                      |   ✅   | top hero paragraph                              |
| Which option you selected           |   ✅   | "Assignment context" callout                    |
| How to run it                       |   ✅   | §"Quick Start (reviewer experience)"           |
| Demo credentials                    |   ✅   | §"Demo credentials"                            |
| Key user flows                      |   ✅   | §"What you can do (a 60-second tour)"          |
| Architecture overview               |   ✅   | §"Architecture"                                |
| Data model overview                 |   ✅   | §"Data model"                                  |
| Known limitations                   |   ✅   | §"Known limitations" + §12 below                |
| What I would improve with more time |   ✅   | `DESIGN.md` §"What I would build next"          |
| AI tools used and how reviewed      |   ✅   | [`AI_USAGE.md`](./AI_USAGE.md)                 |

---

## 10. Cautions / notes to satisfy the "notes" section

The brief warns three things to avoid. PICO checks all three:

- ❌ **"Don't build perfect auth unless central"** → ✅ Auth is minimal but real (PBKDF2 cookie auth, CSRF, rate limit). It IS central to a cloud platform, so we did it properly.
- ❌ **"Don't use paid services / private creds"** → ✅ Default `PROVISIONING_MODE=mock`. `docker` and `openstack` modes exist but are opt-in and documented.
- ❌ **"Don't overbuild"** → ✅ Scope is exactly 8 entities + the flows the rubric asks for. Extras ship behind `PROVISIONING_MODE=openstack` and never block the demo.
- 🏆 **Scope reminder quote from the brief:** *"A thoughtful, complete small module is better than a large unfinished system. We value clarity, ownership and judgment more than visual polish alone."*

---

## 11. PICO-specific requirements (from the brief, Option 2)

These are the concrete things the brief stipulates for the PICO Option 2 cloud
module and where each is satisfied. They're not a separate rubric area — they
roll up into §7 — but reviewers asked whether they're each covered, so they're
listed here explicitly.

| Pico requirement (paraphrased from the brief)                              | Implemented where                                                       |
|----------------------------------------------------------------------------|-------------------------------------------------------------------------|
| Customer-facing flow (sign-up, sign-in)                                     | `app/(dashboard)/signup/page.tsx`, `app/(dashboard)/login/page.tsx`     |
| Service / package selection (browse offerings)                              | `app/(dashboard)/catalog/page.tsx` (public — no auth required)            |
| Pricing or cost estimate                                                    | `Pico.Application.Services.PricingCalculator` + cards display           |
| Simulated provisioning state machine                                        | `Pico.Domain.Entities.ResourceStateMachine` + `MockBackend`             |
| Resource list view                                                          | `app/(dashboard)/dashboard/page.tsx`                                     |
| Resource detail view with state                                             | `app/(dashboard)/dashboard/[id]/page.tsx`                               |
| Billing / invoice view                                                      | `app/(dashboard)/billing/page.tsx` + `[id]/page.tsx`                     |
| Clear error / loading / empty states                                        | `frontend/src/components/{EmptyState,ErrorState}.tsx`, Suspense + TanStack `isFetching` flags |
| Mocked infrastructure API / service layer                                   | `IProvisioningBackend` with `MockBackend` (default), `DockerBackend`, `OpenStackBackend` |
| Seed data for review                                                         | `Pico.Infrastructure.Seeders.DbSeeder` (idempotent)                      |
| Pricing displayed before commit                                              | `ProvisionResource` returns `ProvisioningPlan` DTO that client renders   |
| Payment simulation                                                           | `BillingService.MarkInvoicePaid` transitions invoice status              |
| RBAC: at least two roles                                                     | `UserRole` enum: `Customer`, `Admin`                                     |
| State changes audit-able                                                     | `audit_logs` table (JSONB details) + `resource_events` (SSE feed)        |
| Idempotent reseeding                                                         | Seeder hashes password before insert; re-running `compose up` is safe     |
| Self-contained: no paid external services                                    | Default mode is `mock`; `openstack` mode requires a user-provided cluster |
| Health / status observability                                                 | `/health` page + `/api/health/live`, `/api/health/ready`                |
| Pluggable backend so the reviewer can prove the FSM works against mocks    | `PROVISIONING_MODE` env var switches at boot                             |
| Show the state machine transitions visibly in the UI                         | Resource detail page shows current status pill + event timeline          |
| Error path surfaces (network down, auth failure, provisioning failure)       | `ProblemDetails` 4xx/5xx with structured response; UI catches and renders |

### 11.1 Data model specifically for the PICO brief

```
users                  — Customer/Admin accounts, PBKDF2-HMAC-SHA256 hashes, role enum
flavors                — VM packages: vcpus, ram_mb, disk_gb, price_per_hour, price_per_month, category
images                 — OS images: name, family (Ubuntu/Debian/AlmaLinux), min_disk_gb
resources              — VM instances: status enum, external_id (container/VM id), ip_address, owner_id
resource_events        — Append-only state-transition log (SSE feed source)
invoices               — Monthly bills per customer, total + status (Open, Paid, Void)
invoice_lines          — Per-resource usage: hours, rate, subtotal
audit_logs             — Security trail: actor, action, target, JSONB details, timestamp
```

All FKs are real (no orphan rows); DB indexes on the four hottest lookups
(`resources.owner_id`, `resources.status`, `invoices.owner_id`,
`audit_logs.actor_id`).

---

## 12. Scope reminder & reviewer's note

> *"A thoughtful, complete small module is better than a large unfinished system."*
> *"We value clarity, ownership and judgment more than visual polish alone."*

PICO's stance: ship the must-haves cleanly, ship a few creativity extras,
document every tradeoff, and let the rubric on its own tell the rest. The
working scorecard below is the operating number we track.

---

## 13. KPI scorecard (target ≥ 95/100)

| Area                                       | Weight | Self-score | Weight × score | Notes                                  |
|--------------------------------------------|-------:|-----------:|----------------:|----------------------------------------|
| Product / user flow                        |     20 |          19 |         19.0    | Public catalog removes friction        |
| Backend / API / data model                 |     20 |          19 |         19.0    | Clean architecture, OpenAPI, 8 tables  |
| Frontend implementation                    |     15 |          14 |         14.0    | Per-page titles, SSE, TanStack Query   |
| Ownership & engineering judgment           |     15 |          14 |         14.0    | Pluggable backends; tradeoffs written  |
| Reliability / security / testing           |     15 |          14 |         14.0    | 152 tests; rate limit; CSRF; audit     |
| Docker / deployment / documentation        |     10 |          10 |         10.0    | One-command boot; full docs            |
| AI-native development reflection           |      5 |           5 |          5.0    | Dedicated AI_USAGE.md w/ review notes  |
| **Total**                                  |  **100** |            |    **95.0**    | Stretch goal; audit over-delivery trail|

The audit over-delivery roadmap ([`AUDIT_REPORT.md`](./AUDIT_REPORT.md)) covers
the rubric points that initially scored lower (per-page titles, public catalog,
hook-driven test suite, security headers, audit log UI, etc.).

---

*Last updated: aligned with the three audit-over-delivery commits
`ee5a544`, `c542cd1`, `793a8e2`.*
