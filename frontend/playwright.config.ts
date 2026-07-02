import { defineConfig, devices } from "@playwright/test";

// PLAYWRIGHT_BASE_URL must be set to the URL of a running stack. There is
// no production default — running e2e against a live deployment is a
// deliberate, env-scoped action. For a local stack:
//   PLAYWRIGHT_BASE_URL=http://localhost:3000 npm run e2e
const BASE_URL = process.env.PLAYWRIGHT_BASE_URL;
if (!BASE_URL) {
  throw new Error(
    "PLAYWRIGHT_BASE_URL is required. Set it to the URL of the stack " +
    "under test, e.g. http://localhost:3000 (local docker compose) or " +
    "the public hostname of a deployment you're authorised to test.",
  );
}

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 2 : 1,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: BASE_URL,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  // We don't start the dev server here — `compose up` brings the full
  // stack up; pass PLAYWRIGHT_BASE_URL=http://localhost:3000 for a local
  // stack. CI uses the same env var against an ephemeral deployment.
});
