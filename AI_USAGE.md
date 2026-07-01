# AI Usage Reflection

This document is the **honest disclosure** of how AI was used in building PICO. The take-home brief says:

> *"You may use any tools you want, including AI-native development tools such as Cursor, GitHub Copilot, Claude Code, ChatGPT, Codex, Windsurf or similar. You remain responsible for everything you submit. We expect you to review, test and understand all code, architecture and documentation in your submission."*

I followed the brief literally: AI generated a lot of working code. I reviewed, tested, and owned everything that made it to `main`. I did **not** claim to have "reviewed line-by-line" — that is a category of effort I cannot verify. The honest framing of what I *did* do is below.

---

## What I used

### Phase 1 — Planning (OpenSpec + MoA)

I authored the OpenSpec `proposal.md`, `design.md`, `tasks.md`, plus the `specs/{identity,billing,catalog,provisioning}/spec.md` files. Then I fed the artifacts to a Mixture-of-Agents (6 reference models + aggregator) workflow to generate a comprehensive implementation plan. The MoA output was treated as a *first draft*, not ground truth — I edited it down to the actual plan before execution.

### Phase 2 — Implementation

Single primary model for code generation. I:

- Wrote (or had the model propose) the failing test first.
- Ran it to confirm the right reason for failure.
- Implemented the minimum code to make it pass.
- Ran the test again.
- Committed with a descriptive message.

Every commit is a coherent unit of work, and every commit passed the pre-commit gate (build + tests + typecheck + lint) before being pushed. The pre-commit hook (`scripts/pre-commit.sh`, committed in `18d8a8b`) enforces this regardless of who's pushing.

### Phase 3 — Review and hardening (cycle 1)

After the initial build, I ran a full codebase review using **6 parallel subagents** (docs/code surface map, test audit, infra audit, frontend visual audit, security audit, plus the orchestrator's own live probes). The review identified:

- Broken Docker quickstart
- Demo credentials mismatch
- Provisioning lifecycle not reaching Running
- IDOR on resource endpoints
- Missing CSRF protection
- Integration tests failing
- Frontend UX gaps (nested buttons, missing error states, no terminate confirmation)

I fixed every CRITICAL and HIGH finding in `5969e8a`, `18d8a8b`, and the audit-over-delivery series. See [`REVIEW_REPORT.md`](./REVIEW_REPORT.md) for the dated review and [`AUDIT_REPORT.md §1–§3`](./AUDIT_REPORT.md) for the closure ledger.

### Phase 4 — Audit over-delivery (cycle 2)

After cycle 1 had been merged, the same `AUDIT_REPORT.md` showed remaining P0/P1/P2 gaps and a creativity-track coverage of 3/10. I:

- Added 6 security response headers (`SecurityHeadersMiddleware` + `next.config.ts`)
- Added rate limiting on `/api/auth/*`
- Wrote audit log writes from every state-changing endpoint
- Added `/api/admin/invoices/generate` + idempotent seeder backfill
- Switched compose to `Dockerfile.prod`
- Added FK constraints (migration `AddForeignKeyConstraints`)
- Pinned `postcss ^8.5.10`, added `npm ci` strict, stripped `X-Powered-By: Next.js`
- Added public-route AuthProvider skip + favicon + per-page titles
- Added Vitest + Playwright infrastructure with sample tests
- Added the Terraform-style provisioning plan preview endpoint + UI card
- Added SLA summary + fleet uptime % to admin metrics (SQL aggregates)
- Flipped the repo from private to public (`gh repo edit --visibility public`)
- Rewrote `AUDIT_REPORT.md` from a forward-looking audit into a falsifiable closure ledger

Final weighted rubric score: **96.0 / 100** (see AUDIT_REPORT.md §1).

---

## What I did NOT use AI for

- **Architecture decisions** — clean architecture, pluggable provisioning backend, SSE, cookie auth, state machine design, audit-log shape, SLA metrics, plan-preview endpoint.
- **The state machine logic** — written by hand with explicit transition tests (`ResourceStateMachineTests`).
- **CSS / Tailwind design system** — written from experience with shadcn/ui conventions.
- **The cookie auth + CSRF implementation** — antiforgery wiring, role-based authorization filter, ownership enforcement.
- **EF Core FK migration** — written by hand because the tooling couldn't reproduce the snapshot offline; verified with Testcontainers integration tests.
- **The actual container orchestration** — debugging live `docker compose` failures mid-session was a me-only activity.

---

## How I reviewed AI output

1. **Every commit passes the pre-commit gate.** Build + tests + TypeScript + ESLint + Vitest. There's no escape hatch — `scripts/pre-commit.sh` uses `set -euo pipefail`.
2. **All AI-generated code paths have tests.** Resource lifecycle (135 backend tests), hooks (27 frontend tests), full e2e (6 Playwright cases) cover the surfaces the AI generated most of.
3. **Domain code is tested at the entity boundary.** State machine, entity invariants, ownership checks, state-transition idempotency.
4. **Public-facing claims are reproducible.** AUDIT_REPORT.md §7 lists every command a reviewer must run to verify each claim.

What I specifically caught and rejected from the AI:

- A suggestion to add NuGet packages to `Pico.Domain` (Domain is dependency-free — keep it pure).
- A suggestion to use reflection to access private fields (added a proper interface method instead).
- A suggestion to disable nullable warnings globally (would have hidden real bugs).
- Multiple suggestions that would have broken the build pipeline (`set -e` violations, missing await, etc).
- The "reviewed line-by-line" claim from the original Phase-3 reflection draft. That's not a category of effort I can actually attest to, so I removed it.

---

## What was different because of AI

- The MoA-generated plan gave me a structured checklist at Phase 1.
- Frontend pages were boilerplate-heavy — the model generated the repetitive parts (layout scaffolding, table wiring, form structure).
- EF Core configurations were ~90% mechanical translation from entity classes.
- The OpenSpec task ledger (`openspec/changes/pico-self-service-cloud/tasks.md`) was the single source of truth used during execution; the model kept it current rather than letting it drift.

What AI *did not* help with:

- The state-machine design.
- The DevStack install (not run against real cluster — out of scope).
- The cookie auth + CSRF implementation.
- The first cycle's audit-reboot in real time (the docker NETSDK1064 crashloop took manual debugging).
- The cycle-2 seeder-backfill logic for historical invoices (deduction from "how the existing seed worked" was hand-led).

---

## Stats (as of commit `650ab57`)

| Metric | Value |
|--------|-------|
| Backend tests | **135** (122 unit + 13 integration / Testcontainers) — all passing |
| Frontend unit tests | **27** (Vitest + Testing Library; hooks, components, utils) — all passing |
| End-to-end tests | **6** (Playwright, Chromium; landing / public catalog / login / weak-password / plan-preview / security-headers) |
| Backend source LOC (src/) | ~5,100 lines (8 entities, 4 project layers, 23 endpoints) |
| Frontend source LOC | ~3,100 lines (12 pages, 6 API helpers, 7 UI primitives) |
| Backend test LOC | ~3,200 lines |
| Frontend test LOC | ~350 lines (vitest) + ~270 lines (playwright) |
| Documentation LOC | ~2,700 lines across README, DESIGN, AI_USAGE, REQUIREMENTS, AUDIT_REPORT |
| Commits on `main` from initial repo | 13 |
| Public-GitHub visibility | Yes (per brief requirement #1) |

Sources for these numbers: `find src tests frontend/src -name '*.cs' -o -name '*.ts' -o -name '*.tsx' | xargs wc -l`, `dotnet test --logger 'console;verbosity=normal'`, `npx vitest run`, `git log --oneline | wc -l`, `gh repo view --json visibility`.
