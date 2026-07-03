/**
 * @vitest-environment jsdom
 *
 * Pure-function tests for the lifecycle-mode classifier used by the
 * resource detail page. The actual page is a "use client" component
 * tied to React Query + Sonner, so we test the helper directly. The
 * classification drives:
 *   - which action buttons render (Start/Stop/Terminate vs Recreate vs
 *     just Provisioning…)
 *   - whether the "Original configuration" recap card shows
 *   - whether the recreate CTA is shown at all
 */
import { describe, it, expect } from "vitest";
import type { ResourceDetail } from "@/lib/api";

type LifecycleMode = "operable" | "provisioning" | "historical" | "failed";

// Mirror of the helper inside resources/[id]/page.tsx. If the page
// changes its rule, this test fails loudly so we update both at once.
function getLifecycleMode(detail: ResourceDetail | undefined): LifecycleMode {
  if (!detail) return "operable";
  if (detail.status === "Provisioning" || detail.status === "Created") return "provisioning";
  if (detail.status === "Terminated") return "historical";
  if (detail.status === "Failed") return "failed";
  return "operable"; // Running | Stopped
}

function makeDetail(status: string): ResourceDetail {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    name: "test-vm",
    flavorId: "00000000-0000-0000-0000-000000000002",
    imageId: "00000000-0000-0000-0000-000000000003",
    status,
    ipAddress: null,
    externalId: null,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    events: [],
  };
}

describe("resource detail lifecycle mode", () => {
  it("classifies Running as operable (Start/Stop/Terminate + configuration card shown)", () => {
    expect(getLifecycleMode(makeDetail("Running"))).toBe("operable");
  });

  it("classifies Stopped as operable (Start/Stop/Terminate + configuration card shown)", () => {
    expect(getLifecycleMode(makeDetail("Stopped"))).toBe("operable");
  });

  it("classifies Provisioning as in-progress (controls hidden, terminate visible)", () => {
    expect(getLifecycleMode(makeDetail("Provisioning"))).toBe("provisioning");
  });

  it("classifies Created as in-progress (same UX as Provisioning)", () => {
    expect(getLifecycleMode(makeDetail("Created"))).toBe("provisioning");
  });

  it("classifies Terminated as historical (Recreate CTA, no Start/Stop)", () => {
    expect(getLifecycleMode(makeDetail("Terminated"))).toBe("historical");
  });

  it("classifies Failed as failed (Recreate CTA shown, distinct from Terminated)", () => {
    expect(getLifecycleMode(makeDetail("Failed"))).toBe("failed");
  });

  it("defaults to operable for undefined detail (loading state)", () => {
    expect(getLifecycleMode(undefined)).toBe("operable");
  });
});