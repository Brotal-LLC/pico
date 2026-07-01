import { defineConfig, devices } from "@playwright/test";

const BASE_URL = process.env.PLAYWRIGHT_BASE_URL ?? "https://pico.aamar.cloud";

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
  // stack up against https://pico.aamar.cloud. Run `npm run e2e` only
  // when the live stack is reachable; CI uses `PLAYWRIGHT_BASE_URL`
  // against an ephemeral stack.
});
