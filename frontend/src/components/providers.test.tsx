/**
 * @vitest-environment node
 *
 * Source-shape smoke test for the Sonner <Toaster> configuration in
 * providers.tsx. We deliberately avoid rendering the Providers tree
 * here because that would mount React Query + AuthProvider for no
 * useful signal — what we care about is the literal class string passed
 * to Toaster.
 *
 * Why this exists: the original "richColors" setup rendered toasts with
 * Sonner's built-in white surface in dark mode, making the alert
 * unreadable against the dark page. The fix pins the toast card to our
 * semantic tokens (bg-background, text-foreground, border-border) so the
 * surface flips with next-themes' `.dark` class.
 */
import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

describe("Toaster theming in providers.tsx", () => {
  const source = readFileSync(
    resolve(__dirname, "./providers.tsx"),
    "utf8",
  );

  it("pins the toast surface to our semantic tokens (so dark mode works)", () => {
    // The className string must reference our three flip-able tokens.
    // If anyone reverts to richColors without custom className, this
    // catches it before the next deploy.
    expect(source).toContain("bg-background");
    expect(source).toContain("text-foreground");
    expect(source).toContain("border-border");
  });

  it("wires toastOptions.className into Toaster props", () => {
    expect(source).toMatch(/toastOptions=\{\{[\s\S]*?className:/);
  });

  it("still passes richColors=true so success/error get accent borders + icons", () => {
    expect(source).toContain("richColors");
    // Sanity: richColors and className must coexist
    const classNameIdx = source.indexOf("className:");
    const richColorsIdx = source.indexOf("richColors");
    expect(classNameIdx).toBeGreaterThan(-1);
    expect(richColorsIdx).toBeGreaterThan(-1);
  });

  it("forwards system theme to Sonner so it can follow next-themes", () => {
    expect(source).toMatch(/theme="system"/);
  });
});