# AI Usage Reflection

This document is the honest disclosure of how AI was used in building PICO.

## TL;DR

I used a **Mixture-of-Agents (MoA)** workflow for the planning phase (6 reference models + aggregator) and a **single primary model** for implementation. All output was reviewed, tested, and committed to by me. No code was auto-merged without a build + test pass.

---

## What I used

### Phase 1 — Planning (OpenSpec + MoA)

I authored the OpenSpec proposal, design, and task list. Then I fed the artifacts to a MoA workflow to generate a comprehensive implementation plan. The MoA output was treated as a *first draft*, not ground truth.

### Phase 2 — Implementation

I used a primary model for code generation. I:
- Wrote the failing test first
- Ran it to confirm the right reason for failure
- Implemented the minimum code to make it pass
- Ran the test again
- Committed with a descriptive message

Every commit is a coherent unit of work, and every commit passed unit tests before being pushed.

### Phase 3 — Review and Hardening

After the initial build, I ran a full codebase review (using parallel subagents for backend, frontend, and deployment). The review identified critical issues:
- Broken Docker quickstart
- Demo credentials mismatch
- Provisioning lifecycle not reaching Running
- IDOR on resource endpoints
- Missing CSRF protection
- Integration tests failing
- Frontend UX gaps (nested buttons, missing error states, no terminate confirmation)

I then fixed all critical and high-severity findings in a hardening pass. This document reflects the state after those fixes.

---

## What I did NOT use AI for

- **Architecture decisions** — clean architecture, pluggable provisioning backend, SSE, cookie auth, state machine design.
- **The state machine logic** — written by hand with transition tests.
- **CSS / Tailwind design system** — written from experience with shadcn/ui conventions.
- **DevStack install debugging** — manual SSH work on the actual VM.

---

## How I reviewed AI output

1. **Every commit must build and pass tests.**
2. **Unit tests are a hard gate.** 95 tests covering domain, application, and service layers.
3. **Domain code is tested at the entity boundary.** State machine transitions, entity invariants, ownership checks.

Specifically, I caught and rejected:
- A suggestion to add NuGet packages to Domain (Domain is dependency-free)
- A suggestion to use reflection to access private fields (added proper interface method instead)
- A suggestion to disable nullable warnings
- Multiple suggestions that would have broken the build pipeline

---

## What was different because of AI

- The MoA-generated plan gave me a structured checklist.
- Frontend pages were boilerplate-heavy — the model generated the repetitive parts.
- EF Core configurations were 90% mechanical translation from entity classes.

What AI *did not* help with:
- The DevStack install on the KVM VM
- The SSE endpoint design
- The cookie auth + CSRF implementation
- The codebase review and hardening pass

---

## Stats

| Metric | Value |
|--------|-------|
| Total lines of code (excluding tests) | ~3,500 |
| Lines of tests | ~1,400 |
| Frontend pages | 12 |
| API endpoints | 17 |
| Domain entities | 8 |
| Unit tests | 95 (all passing) |
| Integration tests | 5 (Testcontainers Postgres) |
| Commits | ~15 |
| Lines of documentation | ~2,000 |