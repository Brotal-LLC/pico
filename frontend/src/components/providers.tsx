"use client";

import { useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "next-themes";
import { Toaster } from "sonner";
import { AuthProvider } from "@/components/AuthProvider";

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30 * 1000,
            refetchOnWindowFocus: false,
            retry: 1,
          },
        },
      })
  );

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider attribute="class" defaultTheme="system" enableSystem>
        <AuthProvider>{children}</AuthProvider>
        {/*
         * Sonner toast theming.
         *
         * Default Sonner rendering uses fixed white / light backgrounds
         * that don't follow next-themes' `.dark` class. In dark mode,
         * the toast becomes a bright white card floating over a dark
         * page — exactly the "alert not visible in dark mode" issue.
         *
         * Solution: keep `richColors` (so success/error/info get accent
         * borders + icons) but override the toast surface with explicit
         * class names tied to our semantic tokens. The toast is rendered
         * inside a portal; sonner accepts `toastOptions.className` and
         * `toastOptions.style` for this purpose.
         */}
        <Toaster
          position="top-right"
          richColors
          theme="system"
          toastOptions={{
            // Cards: follow our --color-background / --color-foreground
            // tokens, which flip correctly with next-themes' .dark class.
            className:
              "bg-background text-foreground border border-border shadow-sm",
            descriptionClassName: "text-muted-foreground",
          }}
        />
      </ThemeProvider>
    </QueryClientProvider>
  );
}