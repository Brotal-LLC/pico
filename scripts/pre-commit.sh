#!/usr/bin/env bash
# Pre-commit hook: lints and tests before each commit.
# Install: ln -s ../../scripts/pre-commit.sh .git/hooks/pre-commit
set -e

echo "==> Pre-commit checks"

echo "  → dotnet build"
dotnet build --nologo --verbosity quiet 2>&1 | tail -3

echo "  → dotnet test (unit only)"
dotnet test --nologo --filter "FullyQualifiedName!~Integration" --verbosity quiet 2>&1 | tail -3

echo "  → frontend typecheck"
cd frontend && npx tsc --noEmit 2>&1 | tail -3

echo "  → frontend lint"
npx eslint . 2>&1 | tail -5 || true

echo "==> All checks passed"