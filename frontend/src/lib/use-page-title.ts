"use client";

import { useEffect } from "react";

const APP_NAME = "Pico";
const SEPARATOR = " · ";

/**
 * Sets `document.title` for client components (Next.js App Router `metadata`
 * only works in Server Components). Also handles cleanup on unmount.
 *
 * @example
 *   usePageTitle("Dashboard"); // → "Dashboard · Pico"
 */
export function usePageTitle(title: string | null | undefined): void {
  useEffect(() => {
    if (typeof document === "undefined") return;
    if (!title) {
      document.title = APP_NAME;
      return;
    }
    document.title = `${title}${SEPARATOR}${APP_NAME}`;
  }, [title]);
}
