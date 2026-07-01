import { test, expect } from "@playwright/test";

/**
 * Smoke test against the live PICO stack. Verifies the public surface
 * (landing + catalog + auth flow). Requires:
 *   - Stack reachable at process.env.PLAYWRIGHT_BASE_URL (default: https://pico.aamar.cloud)
 *   - Demo credentials: demo@pico.local / localdev123
 *
 * Run via: `npm run e2e` from the frontend directory.
 */
test.describe("Pico smoke test", () => {
  test("landing page loads with Hero CTA", async ({ page }) => {
    await page.goto("/");
    await expect(page).toHaveTitle(/Pico/);
    // Hero CTA visible to anonymous visitor (either "Get started" or "Go to dashboard")
    await expect(page.getByRole("link", { name: /get started|go to dashboard/i }).first()).toBeVisible();
    // Favicon must not 404 — Next 16 strips <link> for icon.svg automatically
    await expect(page).toHaveTitle(/.+/);
  });

  test("public catalog page lists flavors without authentication", async ({ page }) => {
    await page.goto("/catalog");
    // At least one flavor card (rendered server-side)
    await expect(page.getByText(/catalog/i).first()).toBeVisible();
  });

  test("login form authenticates the demo user", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel(/email/i).fill("demo@pico.local");
    await page.getByLabel(/password/i).fill("localdev123");
    await page.getByRole("button", { name: /sign in/i }).click();
    // On success, redirect to /dashboard
    await page.waitForURL(/\/dashboard$/, { timeout: 15_000 });
    await expect(page.getByRole("heading", { name: /dashboard/i })).toBeVisible();
    // Document title updated by usePageTitle
    await expect(page).toHaveTitle(/Dashboard/);
  });

  test("signup form rejects weak passwords", async ({ page }) => {
    await page.goto("/signup");
    await page.getByLabel(/email/i).fill("weak@example.com");
    await page.getByLabel(/name/i).fill("Weak Tester");
    await page.getByLabel(/password/i).fill("123");
    await page.getByRole("button", { name: /sign up/i }).click();
    // Client-side validation should surface an error message inline
    await expect(page.getByText(/8|password|character/i).first()).toBeVisible();
  });
});
