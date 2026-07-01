import { describe, expect, it } from "vitest";
import { cn, formatBytes, formatCurrency, formatRelativeTime, getErrorMessage } from "@/lib/utils";

describe("cn", () => {
  it("concatenates truthy class names", () => {
    expect(cn("a", "b", "c")).toBe("a b c");
  });

  it("drops falsy values", () => {
    expect(cn("a", false, undefined, null, 0, "", "b")).toBe("a b");
  });

  it("merges conflicting tailwind classes", () => {
    // twMerge keeps the last one
    expect(cn("p-2", "p-4")).toBe("p-4");
  });
});

describe("formatCurrency", () => {
  it("formats USD by default", () => {
    expect(formatCurrency(12.5)).toMatch(/\$12\.50/);
  });

  it("respects the currency arg", () => {
    expect(formatCurrency(12.5, "EUR")).toMatch(/€/);
  });
});

describe("formatBytes", () => {
  it("zero is '0 B'", () => {
    expect(formatBytes(0)).toBe("0 B");
  });

  it("formats KB / MB / GB", () => {
    expect(formatBytes(1024)).toBe("1 KB");
    expect(formatBytes(1024 * 1024)).toBe("1 MB");
    expect(formatBytes(1024 * 1024 * 1024)).toBe("1 GB");
  });
});

describe("formatRelativeTime", () => {
  it("shows seconds for very recent dates", () => {
    const now = new Date();
    expect(formatRelativeTime(now)).toMatch(/s ago$/);
  });

  it("shows 'minutes ago' for older dates", () => {
    const threeMinAgo = new Date(Date.now() - 3 * 60 * 1000);
    expect(formatRelativeTime(threeMinAgo)).toBe("3m ago");
  });
});

describe("getErrorMessage", () => {
  it("returns the Error message", () => {
    expect(getErrorMessage(new Error("boom"))).toBe("boom");
  });

  it("returns the fallback for non-Error values", () => {
    expect(getErrorMessage("oops")).toBe("Something went wrong");
    expect(getErrorMessage("oops", "Custom fallback")).toBe("Custom fallback");
  });

  it("returns the fallback for null/undefined", () => {
    expect(getErrorMessage(null)).toBe("Something went wrong");
    expect(getErrorMessage(undefined)).toBe("Something went wrong");
  });
});
