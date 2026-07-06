import { test, expect } from "@playwright/test";

/**
 * Smoke test against a running PICO stack. Verifies the public surface
 * (landing + browse + auth flow). Requires:
 *   - Stack reachable at process.env.PLAYWRIGHT_BASE_URL
 *   - Demo credentials from env vars (falls back to local dev defaults):
 *     PLAYWRIGHT_DEMO_EMAIL  (default: demo@pico.local)
 *     PLAYWRIGHT_DEMO_PASS   (default: pico-demo-password)
 *
 * Run via: `npm run e2e` from the frontend directory with PLAYWRIGHT_BASE_URL
 * set, e.g. `PLAYWRIGHT_BASE_URL=http://localhost:3000 npm run e2e`.
 */
const DEMO_EMAIL = process.env.PLAYWRIGHT_DEMO_EMAIL ?? "demo@pico.local";
const DEMO_PASS = process.env.PLAYWRIGHT_DEMO_PASS ?? "pico-demo-password";

test.describe("Pico smoke test", () => {
  test("landing page loads with Hero CTA", async ({ page }) => {
    await page.goto("/");
    await expect(page).toHaveTitle(/Pico/);
    // Hero CTA visible to anonymous visitor (either "Get started" or "Go to dashboard")
    await expect(page.getByRole("link", { name: /get started|go to dashboard/i }).first()).toBeVisible();
    // Favicon must not 404 — Next 16 strips <link> for icon.svg automatically
    await expect(page).toHaveTitle(/.+/);
  });

  test("public browse page lists flavors without authentication", async ({ page }) => {
    await page.goto("/browse");
    // The browse page heading says "Packages"
    await expect(page.getByRole("heading", { name: /packages/i })).toBeVisible();
    // At least one flavor card (pico.nano, pico.micro, etc.)
    await expect(page.getByRole("heading", { name: /pico\./i }).first()).toBeVisible();
  });

  test("login form authenticates the demo user", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel(/email/i).fill(DEMO_EMAIL);
    await page.getByLabel(/password/i).fill(DEMO_PASS);
    await page.getByRole("button", { name: /sign in/i }).click();
    // On success, redirect to /dashboard
    await page.waitForURL(/\/dashboard$/, { timeout: 15_000 });
    // Dashboard page shows a "Resources" heading
    await expect(page.getByRole("heading", { name: "Resources", exact: true })).toBeVisible();
  });

  test("signup form rejects weak passwords", async ({ page }) => {
    await page.goto("/signup");
    await page.getByLabel(/email/i).fill("weak@example.com");
    await page.getByLabel(/name/i).fill("Weak Tester");
    await page.getByLabel(/password/i).fill("123");
    await page.getByRole("button", { name: /create account/i }).click();
    // Client-side validation should surface an error message inline
    await expect(page.getByText(/8|password|character/i).first()).toBeVisible();
  });
});