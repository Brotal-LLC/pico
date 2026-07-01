# Implementation plans (historical)

This directory once held five per-finding execution plans (`001`–`005`) written at commit `917e5f2` to drive the cycle-1 audit-over-delivery pass.

| Plan | Topic | Commit resolved in |
|---|---|---|
| 001 | Harden resource lifecycle actions before backend side effects | `5969e8a`, `ad6eb3d`, plus later pre-commit gate additions |
| 002 | Restore the current-user session contract | `5969e8a` (admin role enum serialization) |
| 003 | Enable real CSRF validation for cookie-authenticated mutations | `5969e8a` (`RequireAntiforgeryForUnsafeMethods`) |
| 004 | Clear high-severity dependency advisories | `18d8a8b` (`postcss` override + NuGet upgrades) |
| 005 | Make the pre-commit script a real quality gate | `18d8a8b` (`scripts/pre-commit.sh` with `set -euo pipefail`; no `\|\| true`) |

All five plans are now **DONE**; their per-file "verification commands" listings have been folded into [`AUDIT_REPORT.md §1–§3`](../AUDIT_REPORT.md) and [`openspec/changes/pico-self-service-cloud/tasks.md`](../openspec/changes/pico-self-service-cloud/tasks.md) §13.

The full plan files have been **removed** from the repo to avoid stale "broken-finding" prose; cycle-1 audit evidence lives in [`AUDIT_REPORT.md`](../AUDIT_REPORT.md) and the git log (commits `c92c23a`, `18d8a8b`, `5969e8a`, `ad6eb3d`).

If you need to review a specific plan's original reasoning, the commit bodies contain the affected files and verification commands. For provenance outside the repo, the files lived in this directory from `2026-07-01` until the consolidation in commit `b8da1d8`.
