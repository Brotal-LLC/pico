#!/usr/bin/env bash
# Pre-commit hook: lints and tests before each commit.
# Install: ln -s ../../scripts/pre-commit.sh .git/hooks/pre-commit
set -euo pipefail

echo "==> Pre-commit checks"

echo "  → dotnet build"
dotnet build --nologo --verbosity quiet

echo "  → dotnet test (unit only)"
dotnet test --nologo --filter "FullyQualifiedName!~Integration" --verbosity quiet

echo "  → frontend typecheck"
(cd frontend && npx tsc --noEmit)

echo "  → frontend lint"
(cd frontend && npx eslint .)

echo "  → frontend tests (vitest)"
(cd frontend && npx vitest run --reporter=default)

echo "==> All checks passed"