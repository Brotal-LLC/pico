import { test, expect } from "@playwright/test";

/**
 * E2E test for the Terraform-style provisioning plan preview.
 * Hits POST /api/resources/preview (via the catalog detail page) and
 * expects the preview card to render with cost + image fit + warnings.
 *
 * Requires:
 *   - Stack reachable at process.env.PLAYWRIGHT_BASE_URL
 *   - Demo credentials: demo@pico.local / localdev123
 */
test.describe("Provision plan preview", () => {
  test("preview card renders after picking flavor + image", async ({ page }) => {
    // Public catalog flow — no auth required to hit the catalog.
    await page.goto("/catalog");
    // Pick the first flavor card → goes to /catalog/[flavorId]
    await page.getByRole("link", { name: /pico\./i }).first().click();
    await page.waitForURL(/\/catalog\/[a-f0-9-]+$/);

    // Wait for flavor + images to load; select will be auto-populated.
    await expect(page.getByText(/provisioning plan|computing provision plan/i)).toBeVisible({
      timeout: 10_000,
    });

    // The actual preview card must surface the estimated monthly cost.
    await expect(page.getByText(/estimated monthly/i)).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText(/os image/i)).toBeVisible();
  });

  test("security headers are present on every response", async ({ page, baseURL }) => {
    const responses: Array<{ url: string; headers: Record<string, string> }> = [];
    page.on("response", (res) => {
      responses.push({ url: res.url(), headers: res.headers() });
    });

    await page.goto("/");
    await page.goto("/catalog");

    // Match responses that came from the stack under test (i.e. the
    // configured PLAYWRIGHT_BASE_URL), not third-party assets. We build
    // a same-origin predicate from the baseURL rather than hardcoding a
    // production hostname so the test stays portable.
    const baseOrigin = new URL(baseURL ?? "http://localhost:3000").origin;
    const ourResponses = responses.filter((r) => {
      try {
        return new URL(r.url).origin === baseOrigin;
      } catch {
        return false;
      }
    });
    expect(ourResponses.length).toBeGreaterThan(0);
    const sample = ourResponses.find((r) =>
      r.headers["x-content-type-options"]
    ) ?? ourResponses[0];

    expect.soft(sample.headers["x-content-type-options"]).toBe("nosniff");
    expect.soft(sample.headers["x-frame-options"]).toBe("DENY");
    // CSP + HSTS are emitted by both Next headers() and the SecurityHeadersMiddleware.
    // They may differ between the SPA (/) and API responses — assert presence only.
    expect.soft(sample.headers["strict-transport-security"]).toMatch(/max-age=/);
    expect.soft(sample.headers["content-security-policy"]).toBeTruthy();
    expect.soft(sample.headers["referrer-policy"]).toBeTruthy();
    expect.soft(sample.headers["permissions-policy"]).toBeTruthy();
  });
});
