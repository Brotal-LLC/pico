# Tasks

## 1. Project Scaffolding
- [ ] 1.1 Create .NET 10 solution with 4 projects (Domain, Application, Infrastructure, Api) — verify: `dotnet build` succeeds
- [ ] 1.2 Create Next.js 16 frontend with Tailwind 4 + shadcn/ui patterns — verify: `npm run dev` starts without errors
- [ ] 1.3 Create Docker Compose with Postgres + API + Frontend, non-root containers — verify: `docker compose up --build` starts all services
- [ ] 1.4 Create .gitignore, .editorconfig, directory.build.props, pre-commit hook — verify: `git commit` triggers lint
- [ ] 1.5 Set up Caddy labels for `pico.aamar.cloud` — verify: Caddy routes to frontend and API

## 2. Domain Model & Persistence
- [ ] 2.1 Write domain entity tests (User, Flavor, Image, Resource, Invoice, AuditLog) — verify: tests fail (no entities yet)
- [ ] 2.2 Implement domain entities with value objects — verify: unit tests pass
- [ ] 2.3 Write DbContext configuration tests — verify: tests fail
- [ ] 2.4 Implement PicoDbContext with EF Core Fluent API configs — verify: context tests pass
- [ ] 2.5 Create EF Core migration for initial schema — verify: `dotnet ef migrations add Initial` succeeds
- [ ] 2.6 Write seed data tests — verify: tests fail
- [ ] 2.7 Implement seed data (flavors, images, demo users, sample resources/invoices) — verify: seed tests pass

## 3. Application Layer
- [ ] 3.1 Write provisioning state machine tests — verify: tests fail
- [ ] 3.2 Implement ResourceStateMachine with valid/invalid transition logic — verify: tests pass
- [ ] 3.3 Write pricing calculator tests — verify: tests fail
- [ ] 3.4 Implement PricingCalculator (hourly + monthly estimates) — verify: tests pass
- [ ] 3.5 Write invoice generator tests — verify: tests fail
- [ ] 3.6 Implement InvoiceGenerator (line items from resource usage) — verify: tests pass
- [ ] 3.7 Write DTOs and mapping profiles — verify: `dotnet build` succeeds

## 4. Provisioning Engine
- [ ] 4.1 Write IProvisioningBackend interface + MockProvisioningBackend tests — verify: tests fail
- [ ] 4.2 Implement MockProvisioningBackend (DB-only state simulation, 2-5s delay) — verify: mock tests pass
- [ ] 4.3 Write DockerProvisioningBackend tests — verify: tests fail
- [ ] 4.4 Implement DockerProvisioningBackend (Docker API, container lifecycle) — verify: docker tests pass
- [ ] 4.5 Write OpenStackProvisioningBackend tests — verify: tests fail
- [ ] 4.6 Implement OpenStackProvisioningBackend (Nova API calls) — verify: openstack tests pass
- [ ] 4.7 Implement ProvisioningBackendFactory (mode selection via env var) — verify: factory tests pass
- [ ] 4.8 Write background provisioning service (process queued requests, update status) — verify: integration test passes

## 5. API Layer
- [ ] 5.1 Write auth endpoint tests (signup, login, logout, me) — verify: tests fail
- [ ] 5.2 Implement AuthEndpoints + cookie middleware + role authorization — verify: auth tests pass
- [ ] 5.3 Write catalog endpoint tests — verify: tests fail
- [ ] 5.4 Implement CatalogEndpoints (flavors, images) — verify: catalog tests pass
- [ ] 5.5 Write resource endpoint tests (CRUD + start/stop/terminate + SSE) — verify: tests fail
- [ ] 5.6 Implement ResourceEndpoints + SSE stream — verify: resource tests pass
- [ ] 5.7 Write invoice endpoint tests — verify: tests fail
- [ ] 5.8 Implement InvoiceEndpoints (list, detail, pay) — verify: invoice tests pass
- [ ] 5.9 Write health endpoint tests — verify: tests fail
- [ ] 5.10 Implement HealthEndpoints (backend status, DB connectivity) — verify: health tests pass
- [ ] 5.11 Write admin endpoint tests — verify: tests fail
- [ ] 5.12 Implement AdminEndpoints (users, resources, metrics) — verify: admin tests pass
- [ ] 5.13 Implement FluentValidation for all request DTOs — verify: validation tests pass
- [ ] 5.14 Implement CORS with whitelisted origins — verify: CORS tests pass
- [ ] 5.15 Implement global exception handler + problem details — verify: error handling tests pass

## 6. Frontend — Layout & Auth
- [ ] 6.1 Create app layout (root, auth, dashboard route groups) — verify: `npm run dev` renders layout
- [ ] 6.2 Implement API client with typed fetch + TanStack Query — verify: type-safe, no errors
- [ ] 6.3 Implement auth context + login/signup forms with Zod validation — verify: form validates correctly
- [ ] 6.4 Implement theme provider (light/dark mode) — verify: theme toggle works

## 7. Frontend — Customer Pages
- [ ] 7.1 Build landing/catalog page (flavor cards, pricing, CTA) — verify: page renders seed flavors
- [ ] 7.2 Build provisioning wizard (select flavor → select image → name → confirm → provision) — verify: wizard completes provisioning
- [ ] 7.3 Build resource list page (table with status badges, actions) — verify: list shows user's resources
- [ ] 7.4 Build resource detail page (status, usage charts, events timeline, action buttons) — verify: SSE updates status in real-time
- [ ] 7.5 Build billing/invoice page (invoice list, detail, pay button) — verify: invoices display correctly
- [ ] 7.6 Build health/status page — verify: page shows backend status
- [ ] 7.7 Implement loading/error/empty states for all pages — verify: each state renders correctly

## 8. Frontend — Admin Pages
- [ ] 8.1 Build admin dashboard (metrics cards, resource breakdown) — verify: admin sees aggregate data
- [ ] 8.2 Build admin resource management page (all resources, filter by user/status) — verify: filter works

## 9. Docker & Deployment
- [ ] 9.1 Write backend Dockerfile.dev (non-root, SDK image, hot reload) — verify: container builds and runs
- [ ] 9.2 Write frontend Dockerfile.dev (non-root, Node 20, hot reload) — verify: container builds and runs
- [ ] 9.3 Write compose.yaml (Postgres + API + Frontend + Caddy labels) — verify: full stack starts
- [ ] 9.4 Write .env.example with all required env vars — verify: documented in README
- [ ] 9.5 Test self-contained reviewer experience: `git clone && docker compose up --build` — verify: app works with zero extra config

## 10. Testing & Quality
- [ ] 10.1 Write integration tests with Testcontainers Postgres — verify: full lifecycle test passes
- [ ] 10.2 Write frontend component tests (Vitest + Testing Library) — verify: component tests pass
- [ ] 10.3 Write Playwright E2E smoke test (signup → provision → view → pay) — verify: E2E passes
- [ ] 10.4 Configure pre-commit hook (dotnet format + tsc + eslint + vitest run) — verify: hook runs on commit
- [ ] 10.5 Run full test suite + lint — verify: all green

## 11. Documentation
- [ ] 11.1 Write README.md (what, how to run, demo creds, key flows, architecture, data model, limitations, improvements) — verify: reviewer can follow instructions
- [ ] 11.2 Write DESIGN.md (architecture overview, tradeoffs, assumptions) — verify: explains key decisions
- [ ] 11.3 Write AI_USAGE.md (AI tools used, review process, ownership) — verify: honest reflection
- [ ] 11.4 Add seed data documentation to README — verify: demo credentials documented

## 12. DevStack VM (Stretch)
- [ ] 12.1 Complete DevStack installation on VM — verify: `openstack service list` shows running services
- [ ] 12.2 Configure Pico's OpenStackProvisioningBackend with VM credentials — verify: can list Nova flavors
- [ ] 12.3 Test end-to-end provisioning via OpenStack mode — verify: real VM is created and visible in Horizon
- [ ] 12.4 Document DevStack setup in README — verify: instructions reproducible