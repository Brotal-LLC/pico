import { describe, expect, it } from "vitest";
import { renderHook } from "@testing-library/react";
import { usePageTitle } from "@/lib/use-page-title";

describe("usePageTitle", () => {
  it("sets document.title with the format 'X · Pico' when title is provided", () => {
    renderHook(() => usePageTitle("Dashboard"));
    expect(document.title).toBe("Dashboard · Pico");
  });

  it("sets document.title to 'Pico' when title is null", () => {
    renderHook(() => usePageTitle(null));
    expect(document.title).toBe("Pico");
  });

  it("sets document.title to 'Pico' when title is an empty string", () => {
    renderHook(() => usePageTitle(""));
    expect(document.title).toBe("Pico");
  });

  it("updates document.title when the title changes", () => {
    const { rerender } = renderHook(({ title }) => usePageTitle(title), {
      initialProps: { title: "One" },
    });
    expect(document.title).toBe("One · Pico");
    rerender({ title: "Two" });
    expect(document.title).toBe("Two · Pico");
  });
});
