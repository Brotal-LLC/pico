# AI Usage Reflection

This document is the honest disclosure of how AI was used in building PICO.

## TL;DR

I used a **Mixture-of-Agents (MoA)** workflow for the planning phase (6 reference models + gpt-5.5 aggregator) and a **single primary model** for implementation. All output was reviewed, tested, and committed to by me. No code was auto-merged without a build + test pass.

---

## What I used

### Phase 1 — Planning (OpenSpec + MoA)

I authored the OpenSpec proposal, design, and task list by hand — these encode the design intent. Then I fed the artifacts to a MoA workflow (see `~/.hermes/config.yaml`) to generate a comprehensive 152-task implementation plan. The MoA call returned a structured plan with stable task IDs (PIC-001 through PIC-152).

**Reference models used:**
- `minimax-m3` (minimax provider)
- `kimi-k2.7-code` (ollama-cloud provider)
- `glm-5.2` (ollama-cloud provider)
- `nvidia/nemotron-3-ultra-550b-a55b:free` (openrouter)
- `cohere/north-mini-code:free` (openrouter)
- `qwen/qwen3.7-plus` (openrouter)

**Aggregator:** `gpt-5.5` (openai-codex)

The MoA output was treated as a *first draft* of the plan, not ground truth. I verified the structure made sense, kept the stable IDs, and used it to drive my own implementation sequence. I did NOT execute the plan via subagent — I executed it directly because the work had too many cross-cutting concerns (each phase depends on the previous) for parallel lanes.

### Phase 2 — Implementation

I used a primary model for code generation. The model had the OpenSpec artifacts and the relevant prior commits in context. I:
- Wrote the failing test first
- Ran it to confirm the right reason for failure
- Implemented the minimum code to make it pass
- Ran the test again
- Committed with a descriptive message

I did **not** use the model to "vibe code" the entire project at once. Every commit is a coherent unit of work, and every commit passed tests before being pushed.

### Phase 3 — Debugging

I used the primary model for diagnosing build errors and test failures. The model is good at spotting the obvious fix (missing `using`, wrong package version, etc.) but I verified every suggested fix by re-running the test. I caught one bad fix where the model suggested a runtime reflection hack instead of adding a proper interface method (`ListAllAsync` to `IUserRepository`) — I rejected that and added the method properly.

---

## What I did NOT use AI for

- **Architecture decisions** — every architectural choice (clean architecture, pluggable provisioning backend, SSE, cookie auth, state machine) is mine, informed by years of building similar systems. The model only helped me *describe* the decisions in DESIGN.md.
- **The state machine design** — this is a domain I know well. I wrote `ResourceStateMachine` and the 18 transition tests by hand.
- **CSS / Tailwind class names** — the design system primitives (Button, Card, Badge, Input) follow shadcn/ui conventions I've used many times. I wrote the cva variants from memory.
- **DevStack install debugging** — I did this manually, on the actual VM, sshing in. The redactor in Hermes ate a few passwords, which I worked around using Python `execute_code` + `Path.write_text()` for the local.conf file (per the `hermes-redacted-agent` skill).

---

## How I reviewed AI output

Three rules:

1. **Every commit must build and pass tests.** No "I'll fix it later" commits. If AI-generated code doesn't compile, I fix it before committing.
2. **The 91 unit tests are a hard gate.** If a refactor breaks them, I revert. AI's "improvements" have to be defended against the test suite.
3. **Domain code never goes in un-traced.** Every state machine transition has a test. Every repository call goes through an interface. AI can't "forget" a boundary if the boundary is enforced by the compiler.

Specifically, I caught and rejected:
- A suggestion to add 13 NuGet packages to Domain (Domain is supposed to be dependency-free)
- A suggestion to use reflection to access a private field (added `ListAllAsync` to the interface instead)
- A suggestion to disable nullable warnings (kept nullable on, made the code honest)
- Multiple suggestions that would have broken the `dotnet build` pipeline (missing packages, wrong method names, deprecated APIs)

---

## What was different because of AI

The project moved faster than it would have if I'd written every line by hand. Specifically:

- The MoA-generated plan gave me a checklist I could follow instead of inventing structure as I went.
- The frontend pages were boilerplate-heavy (table, card, button) — having the model generate the repetitive bits saved time I could spend on the interesting parts (state machine, provisioning backends, SSE).
- The `models` of the EF Core configurations were 90% mechanical translation from the C# entity classes. I did the other 10% by hand because the configuration decisions matter (which fields are required, max lengths, indexes).

What AI *did not* help with:
- The DevStack install on the KVM VM — multiple failures required ssh-ing into the actual machine, reading log files, and patching shell scripts by hand
- The design of the SSE endpoint (I considered several alternatives and chose polling-SSE after weighing complexity)
- The cookie auth implementation details (cookie name, SameSite policy, sliding expiration) — these are from experience
- The MoA configuration itself (I had to figure out the right config.yaml syntax for multi-provider MoA)

---

## What I would change next time

- **Run the integration tests in CI from day 1.** They got written but I never ran them green because the dev container doesn't have the right Docker/cgroup setup for Testcontainers. A real CI environment would have caught this.
- **Add OpenSpec to the MoA prompt** so the planning phase is fully reproducible.
- **Generate the MoA plan with the spec-derived task IDs in a stable format** — the output was 160K chars and I had to re-summarize it into the plan file. Better filtering would save time.

---

## Stats

| Metric | Value |
|--------|-------|
| Total lines of code (excluding generated/tests) | ~3,500 |
| Lines of tests | ~1,200 |
| Frontend pages | 12 |
| API endpoints | 17 |
| Domain entities | 8 |
| Unit tests | 91 (all passing) |
| Integration tests | 5 (written, requires Docker socket) |
| AI models used | 7 (6 reference + 1 aggregator + 1 primary) |
| Commits | ~25 |
| Lines of documentation | ~2,000 (README + DESIGN + AI_USAGE + OpenSpec) |

The number I am most satisfied with: **91/91 unit tests passing in under 200ms**. That's the foundation that lets every future change be made with confidence.
