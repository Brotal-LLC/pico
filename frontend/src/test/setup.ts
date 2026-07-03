import { afterEach, vi, expect } from "vitest";
import { cleanup } from "@testing-library/react";
import * as matchers from "@testing-library/jest-dom/matchers";

// Register jest-dom matchers manually (vitest 4.x + jest-dom 6.x
// compatibility: the /vitest entry point uses require() which fails
// because vitest 4.x throws on CJS import).
expect.extend(matchers);

// jsdom doesn't implement matchMedia; next-themes reads it on mount
// even with enableSystem=false (it probes system color-scheme).
// Polyfill with a no-op that defaults to light.
if (typeof window !== "undefined" && !window.matchMedia) {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

// Auto-cleanup React Testing Library rendered components after each test
afterEach(() => {
  cleanup();
});
