/**
 * @vitest-environment node
 *
 * Route-correctness tests for the catalog rename. We use source-shape
 * assertions rather than rendering the actual pages because both pages
 * sit inside Next.js route trees where rendering requires a full
 * app shell (AuthProvider, QueryClientProvider, etc.) — far more
 * scaffolding than the signal we need. The literal source strings are
 * what we want to assert on, and they fail loudly the moment anyone
 * reintroduces the bug.
 *
 * What this guards against (regression after /catalog → /browse split):
 *   1. /catalog serving the public/marketing page instead of the
 *      dashboard shell when logged in (the bug we're fixing).
 *   2. A "Back to dashboard" link appearing inside the dashboard
 *      catalog page (the second half of the user's complaint).
 *   3. The AuthProvider PUBLIC_ROUTES set still naming `/catalog`,
 *      which would skip the auth probe on a now-protected route.
 *   4. The landing page's "Browse packages" CTA pointing at the old
 *      `/catalog` URL, leaving the public surface undiscoverable.
 */
import { describe, it, expect } from "vitest";
import { readFileSync, existsSync } from "node:fs";
import { resolve } from "node:path";

const ROOT = resolve(__dirname, "../../../../");

describe("dashboard /catalog route", () => {
  const dashboardCatalog = resolve(
    ROOT,
    "src/app/(dashboard)/catalog/page.tsx",
  );

  it("exists under the (dashboard) route group so it picks up the sidebar layout", () => {
    expect(existsSync(dashboardCatalog)).toBe(true);
  });

  it("does NOT render a 'Back to dashboard' link (user-visible chrome is the sidebar)", () => {
    const source = readFileSync(dashboardCatalog, "utf8");
    expect(source.toLowerCase()).not.toContain("back to dashboard");
  });

  it("links each flavor's CTA to /catalog/{id} (the dashboard provision detail)", () => {
    const source = readFileSync(dashboardCatalog, "utf8");
    expect(source).toMatch(/href=\{?[`'"]\/catalog\/\$\{/);
  });
});

describe("public /browse route (former /catalog marketing page)", () => {
  const browsePage = resolve(ROOT, "src/app/browse/page.tsx");

  it("exists at the new /browse path", () => {
    expect(existsSync(browsePage)).toBe(true);
  });

  it("no longer renders a 'Back to dashboard' link", () => {
    const source = readFileSync(browsePage, "utf8");
    expect(source.toLowerCase()).not.toContain("back to dashboard");
  });

  it("no longer detects authentication or branches on isAuthenticated", () => {
    // Auth-aware branching was the reason the link existed in the first place.
    // The page is a pure marketing surface now; logged-in visitors land here
    // from shared links and should see the same content as everyone else.
    const source = readFileSync(browsePage, "utf8");
    expect(source).not.toContain("isAuthenticated");
    expect(source).not.toMatch(/cookies\(\)/);
  });

  it("uses the title 'Browse packages' (not 'Catalog')", () => {
    const source = readFileSync(browsePage, "utf8");
    expect(source).toMatch(/title:\s*["']Browse packages["']/);
  });
});

describe("AuthProvider public-route allowlist", () => {
  it("names /browse (not /catalog) so the auth probe runs on the protected route", () => {
    const source = readFileSync(
      resolve(ROOT, "src/components/AuthProvider.tsx"),
      "utf8",
    );
    expect(source).toMatch(/PUBLIC_ROUTES\s*=\s*new Set<string>\(\[\s*[^)]*"\/browse"/);
    expect(source).not.toMatch(/"\/catalog"/);
  });
});

describe("landing page CTA", () => {
  it("links 'Browse packages' to /browse, not /catalog", () => {
    const source = readFileSync(resolve(ROOT, "src/app/page.tsx"), "utf8");
    // Match the literal href inside the Browse packages CTA. Using a
    // narrow regex so we don't accidentally pass if "/catalog" appears
    // somewhere else (e.g. a comment about historical routes).
    expect(source).toMatch(/href=["']\/browse["'][^>]*>\s*Browse packages/);
  });
});

describe("Sidebar nav", () => {
  it("still points at /catalog (now dashboard-only)", () => {
    const source = readFileSync(
      resolve(ROOT, "src/components/Sidebar.tsx"),
      "utf8",
    );
    expect(source).toMatch(/href:\s*"\/catalog"/);
  });
});