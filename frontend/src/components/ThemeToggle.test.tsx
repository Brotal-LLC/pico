import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import { ThemeProvider, useTheme } from "next-themes";
import { ThemeToggle } from "./ThemeToggle";

function ReadThemeProbe() {
  const { resolvedTheme } = useTheme();
  return <span data-testid="resolved">{resolvedTheme ?? "unset"}</span>;
}

function Harness({
  defaultTheme = "light",
  storageKey,
}: {
  defaultTheme?: "light" | "dark" | "system";
  storageKey?: string;
}) {
  return (
    <ThemeProvider
      attribute="class"
      defaultTheme={defaultTheme}
      enableSystem={false}
      storageKey={storageKey ?? `pico-test-${defaultTheme}`}
    >
      <ReadThemeProbe />
      <ThemeToggle />
    </ThemeProvider>
  );
}

describe("ThemeToggle", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.className = "";
  });

  it("renders a button labelled 'Switch to dark theme' when in light mode", async () => {
    render(<Harness defaultTheme="light" storageKey="k1" />);
    await act(async () => {});
    const btn = screen.getByRole("button", { name: /switch to dark theme/i });
    expect(btn).toBeTruthy();
  });

  it("clicking the toggle flips resolvedTheme from light -> dark and toggles <html> class", async () => {
    render(<Harness defaultTheme="light" storageKey="k2" />);
    await act(async () => {});
    expect(screen.getByTestId("resolved").textContent).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);

    const btn = screen.getByRole("button", { name: /switch to dark theme/i });
    await act(async () => {
      fireEvent.click(btn);
    });

    expect(screen.getByTestId("resolved").textContent).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("clicking again flips back to light", async () => {
    render(<Harness defaultTheme="dark" storageKey="k3" />);
    await act(async () => {});
    expect(screen.getByTestId("resolved").textContent).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);

    const btn = screen.getByRole("button", { name: /switch to light theme/i });
    await act(async () => {
      fireEvent.click(btn);
    });

    expect(screen.getByTestId("resolved").textContent).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });
});