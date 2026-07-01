import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

// Auto-cleanup React Testing Library rendered components after each test
afterEach(() => {
  cleanup();
});
