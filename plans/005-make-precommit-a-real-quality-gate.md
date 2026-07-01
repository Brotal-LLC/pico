# Plan 005: Make the pre-commit script a real quality gate

> **Executor instructions**: Follow this plan step by step. Run every verification command and confirm the expected result before moving to the next step. If anything in the "STOP conditions" section occurs, stop and report — do not improvise. When done, update the status row for this plan in `plans/README.md` — unless a reviewer dispatched you and told you they maintain the index.
>
> **Drift check (run first)**: `git diff --stat 917e5f2..HEAD -- scripts/pre-commit.sh frontend/src/components/providers.tsx frontend/src/app/\(dashboard\)/resources/[id]/page.tsx frontend/eslint.config.mjs README.md`
> If any in-scope file changed since this plan was written, compare the "Current state" excerpts against the live code before proceeding; on a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: S
- **Risk**: LOW — shell-script/tooling-only change, but it affects developer workflow.
- **Depends on**: none
- **Category**: dx
- **Planned at**: commit `917e5f2`, 2026-07-01

## Why this matters

The repository advertises a tracked pre-commit hook as part of its reviewer-quality story, but the script can pass even when core checks fail. Pipelines such as `dotnet build ... | tail -3` return the `tail` exit code unless `pipefail` is set, and `npx eslint . ... || true` deliberately ignores lint failure. This weakens every future hardening plan because local automation can say "All checks passed" after a broken build or lint failure.

## Current state

Relevant files:

- `scripts/pre-commit.sh` — tracked quality gate script.
- `frontend/src/components/providers.tsx` — one source of current ESLint warnings.
- `frontend/src/app/(dashboard)/resources/[id]/page.tsx` — one source of current ESLint warnings.
- `frontend/eslint.config.mjs` — current rule configuration.
- `README.md` — documents the pre-commit hook in project structure.

Current excerpts:

```bash
# scripts/pre-commit.sh:4
set -e
```

```bash
# scripts/pre-commit.sh:8-18
echo "  → dotnet build"
dotnet build --nologo --verbosity quiet 2>&1 | tail -3

echo "  → dotnet test (unit only)"
dotnet test --nologo --filter "FullyQualifiedName!~Integration" --verbosity quiet 2>&1 | tail -3

echo "  → frontend typecheck"
cd frontend && npx tsc --noEmit 2>&1 | tail -3

echo "  → frontend lint"
npx eslint . 2>&1 | tail -5 || true
```

Advisor verification output:

```text
cd frontend && npx eslint .
ESLint: 0 errors, 4 warnings in 2 files
Top files:
  src/components/providers.tsx (3 unused-vars warnings)
  src/app/(dashboard)/resources/[id]/page.tsx (1 unused-vars warning)
```

Repo conventions to match:

- The project already has explicit commands in `frontend/package.json`: `typecheck`, `lint`, `test`, and `e2e`.
- The README describes `scripts/pre-commit.sh` as tracked; keep it a simple shell script unless the team asks for Husky/Lefthook/pre-commit framework.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Build | `dotnet build --nologo` | exit 0 |
| Unit tests | `dotnet test --nologo --filter "FullyQualifiedName!~Integration"` | exit 0 |
| Typecheck | `cd frontend && npx tsc --noEmit` | exit 0 |
| Lint | `cd frontend && npx eslint .` | exit 0; ideally 0 warnings after cleanup |
| Hook dry run | `bash scripts/pre-commit.sh` | exit 0 only when every internal check succeeds |

## Scope

**In scope**:

- `scripts/pre-commit.sh`
- `frontend/src/components/providers.tsx` only to remove unused imports
- `frontend/src/app/(dashboard)/resources/[id]/page.tsx` only to remove unused imports
- `README.md` only if install/run instructions need clarification

**Out of scope**:

- Do not add Vitest/Playwright tests in this plan; the scripts currently exist but no test files are present.
- Do not introduce a new hook framework unless explicitly requested.
- Do not change ESLint severity policy broadly.

## Git workflow

- Branch suggestion: `advisor/005-make-precommit-a-real-quality-gate`
- Commit style: `fix: make pre-commit checks fail closed`
- Do not push or open a PR unless instructed.

## Steps

### Step 1: Make the shell script fail on failed pipeline commands

Edit `scripts/pre-commit.sh` to use strict shell settings:

```bash
set -euo pipefail
```

Avoid piping check commands directly into `tail` unless you preserve the original command's exit code. A simple, robust option is to run each command normally and let it stream output. If concise output is important, capture output to a temp file, print a tail on failure, and return the original command status.

**Verify**: Temporarily run a known-failing command pattern locally (for example, change nothing in code but run `bash -c 'set -euo pipefail; false | tail -1'` to confirm your shell understanding). Do not leave any temporary edits behind.

### Step 2: Stop ignoring ESLint failures

Remove `|| true` from the ESLint line. Prefer package scripts for consistency:

```bash
(cd frontend && npx eslint .)
```

or:

```bash
(cd frontend && npm run lint)
```

`npm run lint` currently maps to `eslint .`, so either is acceptable.

**Verify**: `bash scripts/pre-commit.sh` → if current lint warnings are warnings only, the script should still pass; if future lint errors occur, it must fail.

### Step 3: Remove current unused imports so lint output is clean

Clean the warnings advisor observed:

- In `frontend/src/components/providers.tsx`, remove unused `createContext`, `useContext`, and `useEffect` imports from React.
- In `frontend/src/app/(dashboard)/resources/[id]/page.tsx`, remove the unused `ResourceDetail` import if it is no longer required after type inference, or keep it if the compiler uses it. Advisor saw one unused-var warning in this file.

**Verify**: `cd frontend && npx eslint .` → exit 0 and ideally no warnings.

### Step 4: Run the hook and core gates

Run the script exactly as a developer would.

**Verify**:

- `bash scripts/pre-commit.sh` → exits 0 and prints success only after build, unit tests, typecheck, and lint all pass.
- `git status --short` → shows only intended files plus `plans/README.md` status update if applicable.

## Test plan

No application tests are added. This is a tooling plan verified by executing the hook and by direct command gates:

- `dotnet build --nologo`
- `dotnet test --nologo --filter "FullyQualifiedName!~Integration"`
- `cd frontend && npx tsc --noEmit`
- `cd frontend && npx eslint .`

## Done criteria

- [ ] `scripts/pre-commit.sh` uses `set -euo pipefail` or equivalent fail-closed handling.
- [ ] No check in `scripts/pre-commit.sh` is masked by `|| true`.
- [ ] Failed build/test/typecheck/lint would cause a nonzero hook exit.
- [ ] Current ESLint warnings are cleaned up or explicitly documented if intentionally retained.
- [ ] `bash scripts/pre-commit.sh` exits 0 on the clean tree.
- [ ] No application source behavior was changed.

## STOP conditions

Stop and report if:

- The hook takes so long that it is no longer suitable for pre-commit; propose moving slower checks to pre-push/CI instead.
- Cleaning warnings requires behavior changes beyond unused import removal.
- Current excerpts do not match the live script.

## Maintenance notes

When CI is added, reuse the same commands but do not rely on pre-commit as the only gate. Keep the hook fail-closed; if a check is too noisy, fix or reconfigure it rather than masking failures.
