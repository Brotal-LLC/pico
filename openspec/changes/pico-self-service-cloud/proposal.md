# PICO Self-Service Cloud Module

## Why

FGL's assignment asks for a self-service cloud module where a customer can discover services, sign up, choose a resource, understand pricing, provision infrastructure, view usage, and manage billing. We need a complete, well-judged product module — not a large unfinished system — that demonstrates product thinking, backend/API design, frontend usability, data modeling, testing, Docker deployment, and responsible AI-native development.

The hiring manager wants to see infrastructure skills. We demonstrate this through a pluggable provisioning engine that supports three modes: mock (DB simulation, for reviewers), docker (real container provisioning), and openstack (real Nova API calls against a DevStack VM running on the development machine via KVM).

## What changes

- **Customer-facing self-service portal** (Next.js 16 + Tailwind + shadcn/ui patterns) with:
  - Landing/catalog page showing available VM packages (flavors)
  - Sign-up / sign-in flow (mocked auth, simple cookie-based session)
  - Package selection with live pricing estimate
  - Provisioning flow with real-time status transitions (SSE)
  - Resource list & detail view with usage metering
  - Invoice/billing view with payment simulation
  - Service health/status page
  - Empty/loading/error states throughout
  - Dark mode support

- **Backend API** (.NET 10, ASP.NET Core, Minimal API) with:
  - Clean architecture: Domain / Application / Infrastructure / Api layers
  - PostgreSQL via EF Core 10 with migrations
  - Mocked auth (cookie-based, simple user roles: Customer, Admin)
  - RESTful API per aggregate (`/api/catalog`, `/api/resources`, `/api/invoices`, `/api/health`)
  - Provisioning state machine with pluggable backend (`mock`, `docker`, `openstack`)
  - FluentValidation for input
  - Audit log for all provisioning/billing actions
  - Seed data for review

- **Provisioning engine** with three modes:
  - `mock` (default): DB-only state simulation, zero external dependencies. This is what reviewers run.
  - `docker`: Actually creates/manages Docker containers as "VMs" via Docker API. For live demo on rogue.
  - `openstack`: Real Nova API calls against DevStack VM running in KVM on the dev machine. Full infra showcase.
  - API surface follows OpenStack Nova conventions (servers, flavors, images) to show infra thinking

- **Infrastructure** (Docker Compose):
  - Single `compose.yaml` for self-contained local run
  - Postgres + API + Frontend
  - Caddy labels for `pico.aamar.cloud` (local dev via infra caddy)
  - Non-root containers throughout
  - CORS whitelisted for: `pico.aamar.cloud`, `pico.ski.bd`, `localhost:3000`

- **DevStack VM** (KVM on this machine):
  - Ubuntu 24.04 cloud image
  - 16 GB RAM, 4 vCPUs, 100 GB disk
  - DevStack all-in-one installation
  - Pico's `openstack` provisioning mode talks to Nova API via VM IP
  - Isolated network namespace, host untouched

## Impact

- Affected specs: none, new system — all specs created in this change
- Affected code: entire repo (greenfield)
- Breaking change: no (new project)

## Out of scope

- Real payment processing (Stripe, SSLCommerz, etc.) — simulated only
- Multi-tenancy / org hierarchy — single-user scope, admin role for management
- Real OIDC/Keycloak — simple mocked auth per assignment guidance
- Cloudflared tunnel configuration — mentioned in README, configured separately by user
- gRPC, ClickHouse, WebSockets — HTTP REST + SSE for status updates only
- Mobile app — responsive web only