import { test, expect } from "@playwright/test";

/**
 * E2E test for the Terraform-style provisioning plan preview.
 * Hits the catalog detail page and expects the preview card to render
 * with cost + image fit + warnings.
 *
 * Requires:
 *   - Stack reachable at process.env.PLAYWRIGHT_BASE_URL
 *   - Demo credentials from env vars (falls back to local dev defaults):
 *     PLAYWRIGHT_DEMO_EMAIL  (default: demo@pico.local)
 *     PLAYWRIGHT_DEMO_PASS   (default: pico-demo-password)
 */
const DEMO_EMAIL = process.env.PLAYWRIGHT_DEMO_EMAIL ?? "demo@pico.local";
const DEMO_PASS = process.env.PLAYWRIGHT_DEMO_PASS ?? "pico-demo-password";

test.describe("Provision plan preview", () => {
  test.beforeEach(async ({ page }) => {
    // Log in before each test — the catalog detail pages are behind auth.
    await page.goto("/login");
    await page.getByLabel(/email/i).fill(DEMO_EMAIL);
    await page.getByLabel(/password/i).fill(DEMO_PASS);
    await page.getByRole("button", { name: /sign in/i }).click();
    await page.waitForURL(/\/dashboard$/, { timeout: 15_000 });
  });

  test("preview card renders after picking flavor + image", async ({ page }) => {
    // Go to the authenticated catalog page
    await page.goto("/catalog");
    // Click the first "Provision" link → goes to /catalog/[flavorId]
    await page.getByRole("link", { name: /provision/i }).first().click();
    await page.waitForURL(/\/catalog\/[a-f0-9-]+$/);

    // Wait for the provisioning plan section to appear.
    await expect(page.getByText(/provisioning plan/i)).toBeVisible({
      timeout: 10_000,
    });

    // The actual preview card must surface the estimated monthly cost.
    await expect(page.getByText(/estimated monthly/i)).toBeVisible({ timeout: 10_000 });
    // The preview card includes an "Image fits in disk" check.
    await expect(page.getByText(/image fits in disk/i)).toBeVisible();
  });

  test("security headers are present on API responses", async ({ page, baseURL }) => {
    // Derive the API URL from the base URL. The API runs on port 8080
    // locally, or on the api.* subdomain for deployed stacks.
    const baseOrigin = new URL(baseURL ?? "http://localhost:3000").origin;
    const apiBase = baseOrigin
      .replace(":3000", ":8080")
      .replace("//pico.", "//pico-api.");

    const response = await page.request.get(`${apiBase}/api/health`);
    expect(response.ok()).toBeTruthy();
    const headers = response.headers();

    // These headers are emitted on every response regardless of scheme.
    expect.soft(headers["x-content-type-options"]).toBe("nosniff");
    expect.soft(headers["x-frame-options"]).toBe("DENY");
    expect.soft(headers["referrer-policy"]).toBeTruthy();
    expect.soft(headers["permissions-policy"]).toBeTruthy();
    // CSP is always present.
    expect.soft(headers["content-security-policy"]).toBeTruthy();
    // HSTS only present over HTTPS — skip on HTTP (localhost).
    if (apiBase.startsWith("https")) {
      expect(headers["strict-transport-security"]).toMatch(/max-age=/);
    }
  });
});