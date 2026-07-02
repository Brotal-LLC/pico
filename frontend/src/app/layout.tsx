import type { Metadata } from "next";
import { Inter, JetBrains_Mono, Newsreader } from "next/font/google";
import { Providers } from "@/components/providers";
import "./globals.css";

/**
 * next/font/google self-hosts the fonts at build time so the production
 * site does not hit fonts.googleapis.com on first paint. Each font exposes
 * a CSS variable (--font-<name>) that globals.css wires to the Tailwind
 * theme tokens via @theme { --font-sans: var(--font-sans-loaded), ... }.
 *
 * If you change the family list here, also update globals.css so the
 * corresponding Tailwind theme token resolves to var(--font-<x>-loaded).
 */
const inter = Inter({
  subsets: ["latin"],
  variable: "--font-sans-loaded",
  display: "swap",
});

const newsreader = Newsreader({
  subsets: ["latin"],
  variable: "--font-display-loaded",
  display: "swap",
  weight: ["400", "500", "600", "700"],
});

const jetbrainsMono = JetBrains_Mono({
  subsets: ["latin"],
  variable: "--font-mono-loaded",
  display: "swap",
});

export const metadata: Metadata = {
  title: {
    default: "Pico — Self-Service Cloud",
    template: "%s · Pico",
  },
  description: "Provision, manage, and monitor your cloud resources",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html
      lang="en"
      suppressHydrationWarning
      className={`${inter.variable} ${newsreader.variable} ${jetbrainsMono.variable}`}
    >
      <body>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}