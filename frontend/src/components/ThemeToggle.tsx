"use client";

import { useEffect, useState } from "react";
import { useTheme } from "next-themes";
import { Sun } from "lucide-react";
import { Button, buttonClass } from "@/components/ui/Button";
import { AnimatedThemeToggler } from "@/components/ui/animated-theme-toggler";

/**
 * ThemeToggle — header control that flips between light and dark using
 * next-themes for persistence and the Magic UI AnimatedThemeToggler
 * for the view-transition reveal animation.
 *
 * Renders a neutral placeholder (with project Button styling) until
 * mounted to avoid hydration mismatch with the `light` / `dark` class
 * that next-themes writes to <html> on the server.
 */
export function ThemeToggle() {
  const [mounted, setMounted] = useState(false);
  const { resolvedTheme, setTheme } = useTheme();
  useEffect(() => setMounted(true), []);

  if (!mounted) {
    return (
      <Button variant="ghost" size="icon" aria-label="Toggle theme" disabled>
        <Sun className="h-4 w-4" />
      </Button>
    );
  }

  const isDark = resolvedTheme === "dark";

  return (
    <AnimatedThemeToggler
      theme={isDark ? "dark" : "light"}
      onThemeChange={setTheme}
      duration={500}
      variant="circle"
      className={buttonClass({ variant: "ghost", size: "icon" })}
    />
  );
}