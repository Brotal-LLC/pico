# Pico Frontend Revamp — full SaaS overhaul with shadcn + Magic UI + editorial typography

> **For Hermes:** Use subagent-driven-development skill to implement phase by phase. Each phase = one feature branch + one PR.

**Goal:** Replace the current minimal-Tailwind Pico frontend with a polished SaaS UI: editorial typography (drop JetBrains Mono for prices/health, use a refined sans with tabular numerals), shadcn primitives (Dialog, Sheet, Tabs, Select, Dropdown, Tooltip, Separator) on Radix UI primitives, Magic UI animated components (BentoGrid, NumberTicker, MagicCard, ShimmerButton, Marquee, AuroraText) for the landing page, intentional motion system (framer-motion spring transitions, view-transitions on route changes, staggered list reveals), and a redesign of every dashboard page.

**Architecture:** Server components by default (Next.js 16 App Router), with `"use client"` islands for interactivity (toggles, dropdowns, forms). Add a `components/motion/` directory holding framer-motion variants + reusable transition wrappers. Keep the existing token system (`@theme` + `.dark` override) — only add new tokens, don't refactor existing color names.

**Tech Stack:** Next.js 16.2.6 (App Router, RSC), React 19.2.6, Tailwind v4.3, shadcn-on-Radix (single `radix-ui` package, post-Feb-2026), Magic UI (vendored from source — no official npm package), framer-motion (new), next-themes (already in), lucide-react (already in).

---

## Decisions (signed off 2026-07-02 by Shakib)

- **D1.** **Full revamp, all pages** — landing + auth + dashboard (health, billing, catalog, admin, resources, dashboard home) + design-system page.
- **D2.** **Official packages + shadcn registry for Magic UI** — `radix-ui` (unified), `framer-motion`, `motion`, `class-variance-authority`, `tailwind-merge` from npm. shadcn primitives installed via the shadcn CLI with the Magic UI registry added (`pnpm dlx shadcn@latest add https://magicui.design/r/<component>.json`). Phase 1 use the shadcn CLI workflow (adds `components.json`, generates canonical primitive files); we override / customize each generated file in-place after install rather than vendoring blindly.
- **D3.** **Typography = proper SaaS feel** — body in `Inter` (current); numbers in body sans but `font-variant-numeric: tabular-nums lining-nums` so they align column-wise without looking like code; **drop JetBrains Mono entirely except for actual IP addresses and resource IDs** (where monospace is correct). Add one serif display face (`Newsreader`) for marketing headlines (landing page hero, section titles) — not used inside the dashboard.
- **D4.** **shadcn primitives installed via `shadcn@latest add`** — generates canonical files in `src/components/ui/` per `components.json`. After install, each primitive is customized in-place: CVA variants added, `data-slot` attributes confirmed, project `Button` styling integrated. The shadcn CLI does the boilerplate; we own the customization. Magic UI components pulled via the same CLI using the Magic UI registry URL.
- **D5.** **Motion philosophy = "intentional, not theatrical"** — every transition has a reason (feedback, hierarchy, delight). No bouncing / wiggling on critical-path controls (toggles, form submits). Micro-interactions on hover (MagicCard spotlight), on click (ShimmerButton ripple), on data change (NumberTicker spring tween). Page transitions = fade + 4px upward translate, 240ms ease-out. List reveals = staggered 60ms fade-in.
- **D6.** **Dashboard layout uses shadcn `Sidebar` primitive** (collapsible icon-rail, persistent on `lg+`, sheet on `<lg`). Theme toggle moves into the sidebar footer (not the header).
- **D7.** **Theme = next-themes (existing)** stays. The existing animated theme toggle from `8c073f9` is preserved.
- **D8.** **One commit per phase, all on a `feat/frontend-revamp` branch (not `main`).** Per D10, the user reviews the live `pico.aamar.cloud` after all phases are done, then approves merge to `main`. No mid-stream pauses. After all phases complete and the live site is verified working, I push `feat/frontend-revamp` to the remote; the user then runs `git -C ~/repos/pico checkout feat/frontend-revamp` to switch the main worktree to the branch (so docker compose picks up the new files), then I rebuild + recreate the docker image to ship the new design live.
- **D9.** **Out of scope** — backend changes, new API endpoints, auth changes, design-system page content rewrites (only the chrome around it gets the new primitives), color palette changes (the existing mono accent + ink scale stays), copy changes (the existing copy stays verbatim).

---

## Phase 0 — Bootstrap (foundation, ~½ day)

Get the dependencies, tokens, and motion primitives in place so the per-page phases are pure swap-in work.

### Phase 0.1 — Install packages

```bash
unset POSTGRES_PASSWORD
cd ~/repos/pico/frontend
npm install radix-ui framer-motion clsx tailwind-merge
# radix-ui is the unified post-Feb-2026 package (replaces @radix-ui/react-* for shadcn)
```

Verify: `cat package.json` shows the new deps in `dependencies`.

### Phase 0.2 — Extend the Tailwind theme with motion + type tokens

File: `src/app/globals.css` (modify the `@theme` block — keep all existing tokens).

Add to the `@theme` block:

```css
@theme {
  /* existing tokens untouched */

  /* Type scale — names match Tailwind v4 convention */
  --font-display: "Newsreader", "ui-serif", "Georgia", "serif";
  --font-sans: "Inter", "ui-sans-serif", "system-ui", "sans-serif";

  /* Tabular numbers utility — used for prices, stats, table cells.
     Falls through to the body sans; numbers align without looking like code. */
  --font-feature-tabular-nums: "tnum";
  --font-feature-lining-nums: "lnum";

  /* Motion easings (ms + cubic-bezier pairs for framer + CSS use) */
  --ease-standard: cubic-bezier(0.2, 0, 0, 1);
  --ease-emphasized: cubic-bezier(0.3, 0, 0, 1);
  --ease-decelerate: cubic-bezier(0, 0, 0, 1);
  --duration-quick: 120ms;
  --duration-standard: 200ms;
  --duration-emphasized: 320ms;
  --duration-page: 240ms;
}
```

Add a `@layer base` block:

```css
@layer base {
  /* Tabular numerals — applied via .tabular-nums utility OR globally on
     <td>/<th>/<data> where it matters for alignment. */
  .tabular-nums { font-variant-numeric: tabular-nums lining-nums; }

  /* Display serif — for marketing headlines only. Not used in dashboard. */
  .font-display { font-family: var(--font-display); font-feature-settings: "ss01", "ss02"; }
}
```

### Phase 0.3 — Add Newsreader via `next/font/google`

File: `src/app/layout.tsx` (modify)

```tsx
import { Inter, Newsreader, JetBrains_Mono } from "next/font/google";

const inter = Inter({ subsets: ["latin"], variable: "--font-sans-loaded" });
const newsreader = Newsreader({ subsets: ["latin"], variable: "--font-display-loaded", weight: ["400","500","600","700"] });
const jetbrainsMono = JetBrains_Mono({ subsets: ["latin"], variable: "--font-mono-loaded" });

// In <html>: <html className={`${inter.variable} ${newsreader.variable} ${jetbrainsMono.variable}`} ...>
// Then in globals.css @theme, swap the static family for the CSS var:
//   --font-sans: var(--font-sans-loaded), "Inter", "ui-sans-serif", ...;
//   --font-display: var(--font-display-loaded), "Newsreader", ...;
//   --font-mono: var(--font-mono-loaded), "JetBrains Mono", ...;
```

Why: `next/font/google` self-hosts the fonts, eliminates the external CDN hit, and gives us proper `font-display: swap` behavior so there's no FOUT.

### Phase 0.4 — Create the motion primitive library

New file: `src/components/motion/variants.ts`

```ts
import type { Variants, Transition } from "framer-motion";

export const easeStandard: Transition["ease"] = [0.2, 0, 0, 1];
export const easeEmphasized: Transition["ease"] = [0.3, 0, 0, 1];

export const fadeUp: Variants = {
  hidden: { opacity: 0, y: 8 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.24, ease: easeStandard } },
};

export const fadeIn: Variants = {
  hidden: { opacity: 0 },
  visible: { opacity: 1, transition: { duration: 0.2, ease: easeStandard } },
};

export const staggerContainer: Variants = {
  hidden: {},
  visible: { transition: { staggerChildren: 0.06, delayChildren: 0.04 } },
};

export const popIn: Variants = {
  hidden: { opacity: 0, scale: 0.96 },
  visible: { opacity: 1, scale: 1, transition: { duration: 0.18, ease: easeEmphasized } },
};
```

New file: `src/components/motion/page-transition.tsx` — wraps `children` in a framer-motion `<motion.div>` that fades + 4px upward translate on mount. Used in every page root.

New file: `src/components/motion/reveal.tsx` — `BlurFade` (vendored from Magic UI): children with `useInView` reveal once on scroll-into-view. `viewport={{ once: true, amount: 0.2 }}`.

New file: `src/components/motion/number-ticker.tsx` — re-animating spring tween (per the SV skill's pattern — the off-the-shelf MagicUI NumberTicker won't re-tween on subsequent value changes).

### Phase 0.5 — Update Button primitive for the new system

File: `src/components/ui/Button.tsx` (modify)

Add three new variants: `shimmer` (uses ShimmerButton vendor in Phase 1), `link` (underline-on-hover), `gradient` (accent gradient border). Keep existing variants intact — they remain the default.

Add `size: "sm" | "md" | "lg" | "icon" | "icon-sm"` (icon-sm = 36px square for in-table use).

### Verification (Phase 0)

- `npx tsc --noEmit` clean
- `npx eslint .` clean
- `npx vitest run` — existing 30 tests still pass (no test changes in Phase 0)
- `bash scripts/pre-commit.sh` from repo root passes (Phase 0 is pre-commit clean)
- Live: rebuild frontend image, open `/login` — Inter still renders, Newsreader loads (visible in DevTools Network → font), no layout shift.

**Commit:** `feat(revamp): add shadcn + framer-motion deps, editorial typography, motion primitives`

---

### Phase 1 — Vendored shadcn primitives + Magic UI components (~1 day)

Phase 1a — shadcn primitives (one commit each, installed via `shadcn@latest add`):

```
# .components.json will be created in Phase 1.0 (init). Then:
cd ~/repos/pico/frontend
pnpm dlx shadcn@latest add card separator badge spinner input skeleton sheet dropdown-menu select tabs tooltip dialog label textarea
```

Each primitive is reviewed + customized in-place after install:
- Add CVA variants not provided by the default template.
- Confirm `data-slot` attributes match the canonical shadcn names.
- Wire imports to use the project `cn` (in `src/lib/utils.ts`) instead of `clsx` + `tailwind-merge` direct.

Phase 1b — project-specific primitives (one commit each, hand-written):

- **Phase 1.8 — Stat** (new, for dashboard). Replaces all `font-mono text-2xl font-bold` patterns. Uses `tabular-nums lining-nums` for column alignment without the code-font look.
- **Phase 1.20 — Timeline** (new, for health page). Vertical incident timeline.

Phase 1c — Magic UI registry components (one commit each, installed via the shadcn Magic UI registry):

```bash
# One-time: add the Magic UI registry to components.json (Phase 1.0 setup)
# Per-component:
pnpm dlx shadcn@latest add https://magicui.design/r/number-ticker.json
pnpm dlx shadcn@latest add https://magicui.design/r/magic-card.json
pnpm dlx shadcn@latest add https://magicui.design/r/shimmer-button.json
pnpm dlx shadcn@latest add https://magicui.design/r/bento-grid.json
pnpm dlx shadcn@latest add https://magicui.design/r/marquee.json
pnpm dlx shadcn@latest add https://magicui.design/r/aurora-text.json
pnpm dlx shadcn@latest add https://magicui.design/r/blur-fade.json
```

**CRITICAL pitfall — the off-the-shelf MagicUI NumberTicker won't re-tween after the first value.** Per the SV skill, replace it with a re-animating variant: the `useSpring` + `useMotionValue` + `useEffect` pattern that resets the previous value on every change. Edit the generated `number-ticker.tsx` in-place immediately after install.

**CRITICAL pitfall — ShimmerButton renders as `<button>` by default. Nesting `<a>` inside breaks clicks.** Per the marketing-site skill, patch the generated `shimmer-button.tsx` so when `href` is provided, it renders as `<a>` directly. The default is unchanged for callers that don't pass `href`.

### Verification (Phase 1)

- Each new primitive has a smoke test (`src/components/ui/<name>.test.tsx`) verifying: renders without crashing, applies correct variant classes, passes aria attributes.
- `npx tsc --noEmit` clean, `npx eslint .` clean.
- `npx vitest run` — existing 30 tests still pass + new primitive tests pass.
- Live: rebuild frontend image, load `/login` — no JS errors in console.

**Commit per primitive (1.1 through 1.20).** Allows bisecting regressions.

---

## Phase 2 — Dashboard shell: Sidebar + Topbar (~½ day)

The persistent shell for every `/dashboard/*` route.

### Phase 2.1 — New Sidebar (collapse on mobile, persistent on lg+)

File: `src/components/Sidebar.tsx` (rewrite).

Pattern:
- `lg+`: vertical 240px rail. Logo + nav + user + theme toggle. Sticky.
- `<lg`: hidden by default; hamburger button in header opens a `Sheet` (from Phase 1.9) with the same nav.
- Active route = nav link has `bg-accent/10 text-accent border-l-2 border-accent`.
- Hover = `bg-muted` (subtle, not filled).
- Bottom: user avatar + dropdown (Profile, Theme toggle, Sign out).

### Phase 2.2 — Remove the per-page header bar

The `(dashboard)/layout.tsx` currently renders its own `<header>` with `<ThemeToggle />`. Replace with the sidebar's persistent footer slot for the toggle. Keep a thin sticky topbar on `lg+` only for breadcrumbs + page actions (e.g. "Provision" CTA on the resources page).

### Phase 2.3 — Apply `PageTransition` to every dashboard page

Wrap each `(dashboard)/*/page.tsx` export in `<PageTransition>` (from Phase 0.4). Use server-component-friendly wrapping (pass `children` as a prop, not as the page itself, to avoid serializing motion config through RSC).

### Verification (Phase 2)

- Open every dashboard page on `lg+` (1280×800) and `<lg` (390×844). Sidebar visible / drawer works.
- Click each nav link — active state highlights correctly.
- Click theme toggle in sidebar footer — flips and persists (existing behavior from `8c073f9`).
- Hard reload on a deep dashboard URL — sidebar still highlights the correct active item.

**Commit:** `feat(revamp): collapsible dashboard sidebar + sticky topbar + page transitions`

---

## Phase 3 — Dashboard pages redesign (~2 days)

One phase per page so each can ship independently.

### Phase 3.1 — Dashboard home (`/dashboard`)

File: `src/app/(dashboard)/dashboard/page.tsx` (rewrite).

Current state: a heading + "No resources yet" + "Browse catalog" link. Page 1 of the user-facing experience.

New layout:
- Page hero: greeting + (for users with resources) the top-line stats row using `<Stat>` (active count, monthly spend, uptime, last activity).
- Quick actions: 3-4 `<MagicCard>` tiles (Provision new, Browse catalog, View billing, Health).
- Recent activity: `<Skeleton>` while loading, then a table (use shadcn-style `<Table>` primitive — Phase 1 followup if not vendored, otherwise build inline with the table primitive from the existing `ui/Table.tsx` if already present).

### Phase 3.2 — Catalog list (`/catalog`)

File: `src/app/catalog/page.tsx` (rewrite).

Current: list of flavors with `<span className="font-mono text-lg font-semibold">` prices.

New:
- Filter bar: `<Select>` (provider), `<Select>` (region), search input (debounced 200ms).
- Layout: 2-column on `lg+` (filters sticky on left 240px, grid on right). Single column on mobile.
- Card: `<MagicCard>` with image placeholder + flavor name + price (using `<Stat>` pattern, not raw `font-mono`) + "Configure" CTA.
- Prices use `tabular-nums lining-nums` (no mono). Strikethrough old price when on promo (none today, but the pattern is there).

### Phase 3.3 — Catalog detail (`/catalog/[id]`)

File: `src/app/(dashboard)/catalog/[id]/page.tsx` (rewrite).

Current: 297 lines of mixed tabs + tables + price blocks.

New:
- Page hero: flavor name (large serif or sans-semibold) + 1-line description + "Provision" primary CTA.
- Pricing block: 3 `<Stat>` cards side-by-side (Hourly, Monthly, Annual equivalent). Tabular numerals.
- Specs: `<Tabs>` (Compute / Storage / Network) instead of the current raw sections.
- Bottom: "Recommended pairings" (placeholder empty state for now).

### Phase 3.4 — Resources list (`/resources` is currently under `(dashboard)/resources`)

File: new — there is no list page today, only a detail page. Add `src/app/(dashboard)/resources/page.tsx` with a `<Table>` listing each resource (id, name, status pill, region, action menu).

### Phase 3.5 — Resource detail (`/resources/[id]`)

File: `src/app/(dashboard)/resources/[id]/page.tsx` (rewrite).

Current: 284 lines, mixed stats + tables + event timeline.

New:
- Page hero: resource name + status pill (Badge variant) + IP address (mono, this is one of the few correct mono uses) + actions menu (DropdownMenu: Reboot, Stop, Delete…).
- Live stats row: CPU / Memory / Network (in / out) using `<Stat>` with `<NumberTicker>` for the live values. Polling every 5s (existing TanStack Query setup).
- Tabs: Overview / Metrics / Events / Logs (Tabs primitive). Metrics tab uses a `<Skeleton>` while loading, then a `<Table>` of samples.

### Phase 3.6 — Billing list (`/billing`)

File: `src/app/(dashboard)/billing/page.tsx` (rewrite).

Current: a table of invoices.

New:
- Top row: 3 `<Stat>` cards (This month so far, Last month, Outstanding).
- Filter bar: date range (use shadcn calendar primitive, vendored in Phase 1 followup if needed), status (Select).
- Table: invoices with proper monospace ONLY for the ID column (UUIDs are correct mono), tabular-nums lining-nums for amount and due-date.

### Phase 3.7 — Billing detail (`/billing/[id]`)

File: `src/app/(dashboard)/billing/[id]/page.tsx` (rewrite).

Current: 131 lines, mostly `<td className="font-mono">` lines.

New:
- Page hero: invoice number (mono — invoice IDs are correct mono) + status pill + total (large `<Stat>`).
- Line items table: tabular-nums for hours + rates + amounts.
- Bottom: download PDF button (ShimmerButton if it's the primary CTA, regular Button otherwise).

### Phase 3.8 — Health (`/health`)

File: `src/app/(dashboard)/health/page.tsx` (rewrite).

Current: 86 lines, mostly `<div className="font-mono">` for the SLA stats.

New:
- Top row: 3-4 `<Stat>` cards (Overall uptime, MTTR, Active incidents, Last incident). `<NumberTicker>` on the uptime percentage.
- Incident timeline: a vertical timeline component (build inline — a new `<Timeline>` primitive in Phase 1.21 if not vendored). Each entry: severity badge + title + duration + resolved-by.
- Status by service: `<Table>` with the existing data, tabular-nums for the percentage.

### Phase 3.9 — Admin (`/admin`)

File: `src/app/(dashboard)/admin/page.tsx` (rewrite).

Current: 125 lines, a top-line stat row + audit log table.

New:
- Tabs (Phase 1.12): Overview / Users / Audit Log / System.
- Overview: `<Stat>` cards (Total users, Active sessions, Failed logins, API keys).
- Users: searchable table + "Invite user" button (shadcn Dialog with form).
- Audit log: virtualized table (use `react-window` — new dep, lightweight, ~6kb gzipped) for the long list. Status filter, severity filter, date range.

### Verification (Phase 3)

- Every page renders without console errors.
- Every page renders correctly at 390×844 and 1280×800.
- No `font-mono` usage remains on prices, percentages, hours, rates, or any tabular numeric value — `rg "font-mono" src/app/\(dashboard\)` returns only IP addresses and resource/invoice UUIDs.
- `npx tsc --noEmit` clean, `npx eslint .` clean, `npx vitest run` — 30 existing + new tests pass.
- `bash scripts/pre-commit.sh` passes.

**Commit per page (3.1 through 3.9).**

---

## Phase 4 — Landing page rebuild (~1 day)

The public marketing surface.

### Phase 4.1 — Hero

File: `src/app/page.tsx` (rewrite).

Pattern (Nike-inspired but adapted to SaaS — no shoe imagery):
- Eyebrow: small uppercase text "Self-Service Cloud" + accent dot.
- Headline: 1 line, large serif (Newsreader) using `<AuroraText>` at low opacity. e.g. "Cloud, on your terms."
- Subhead: 1 line, max 50 words. Punchy.
- CTA row: `<ShimmerButton href="/signup">Start free</ShimmerButton>` + `<Button variant="outline" href="/login">Sign in</Button>`.
- Right side / below: animated 3D-ish card grid (use `<BentoGrid>` with 3-4 cells: "Provision in 60s", "Per-second billing", "Open-source engine", "RBAC + audit").

### Phase 4.2 — Why Pico (features grid)

`<BentoGrid>` of 6 `<MagicCard>` tiles. Each tile: icon (lucide), title (Inter semibold), 1-sentence description, optional link. Pattern from Magic UI's "Features" demo.

### Phase 4.3 — Pricing preview

File: new section in `page.tsx`.

3-column grid of pricing cards. Numbers use `<Stat>` (tabular-nums lining-nums, no mono). Primary CTA per card = `<ShimmerButton>`. Highlighted middle card = `border-accent` + small "Most popular" Badge.

### Phase 4.4 — Customer logos / testimonials

If `data/content.json` or backend has any testimonials — use `<Marquee>`. If not, drop this section (don't fabricate logos).

### Phase 4.5 — Footer

Replace the existing minimal footer with a proper 4-column footer: Product / Company / Resources / Legal. Bottom row: copyright + social links (placeholder href if no accounts). No "EMI/payment-plan" language anywhere (per user style rules in memory).

### Verification (Phase 4)

- Live: rebuild frontend, open `https://pico.aamar.cloud/` on desktop + mobile.
- Hero renders with Newsreader headline (visible in DevTools).
- `ShimmerButton` CTA navigates to `/signup` correctly (per the marketing-site skill pitfall: render as `<a>` directly when `href` is set).
- All interactive elements have visible focus rings (per the existing `focus-visible:` rules).
- No console errors.

**Commit:** `feat(revamp): landing page rebuild with editorial typography + animated components`

---

## Phase 5 — Auth pages polish (~½ day)

`/login` and `/signup` are the first pages users hit. They should feel intentional, not afterthought.

### Phase 5.1 — `/login`

File: `src/app/(auth)/login/page.tsx` (rewrite).

Pattern:
- Centered card (max-width 28rem), generous padding (`p-8 md:p-10`), no nested cards, no glass.
- Logo + heading + 1-line subhead.
- Form: email (Input with startIcon), password (Input with endIcon for show/hide), Submit (ShimmerButton, full-width).
- Bottom: "Don't have an account? Sign up" link.

### Phase 5.2 — `/signup`

File: `src/app/(auth)/signup/page.tsx` (rewrite). Same pattern as login, plus a name field.

### Verification (Phase 5)

- Live: login flow still works (existing API integration unchanged). Log in as `admin@pico.local` / `pico-admin-password` and land on `/dashboard` with the new sidebar.

**Commit:** `feat(revamp): polished auth pages (login + signup)`

---

## Phase 6 — Design-system page update (~¼ day)

`src/app/design-system/page.tsx` (currently not vendored / present? let me verify — if not present, skip this phase).

If a design-system page exists, add a section for every new primitive vendored in Phase 1, with the live demo + a 3-column "Shape / Behaviour / Sourced-from" block (per the SV skill pattern).

If the page does not exist, create it as the canonical reference. Skip if the user's standing rule "design-system page must mirror actual components — update in same session" doesn't apply because we have no current page.

---

## Phase 7 — Visual QA + accessibility pass (~½ day)

This is the phase where the user (Shakib) live-verifies with screenshots, per memory rule "pre-push: tests pass + for UI changes push a real DM demo and wait for user visual confirm".

### Tasks

1. Rebuild frontend image, restart container.
2. `browser_navigate` to every route in the app at 390×844 and 1280×800. Screenshot each.
3. Send screenshots to user via DM. Wait for "looks good" or revision requests.
4. `npx axe http://localhost:3000` (or run `@axe-core/playwright` via the existing playwright setup) — fix any AA violations.
5. Keyboard-only navigation check: every interactive element reachable via Tab, all dialogs trap focus, Esc closes.
6. Reduced-motion check: `prefers-reduced-motion: reduce` — verify all framer-motion transitions are short or disabled.
7. Final `bash scripts/pre-commit.sh` — must be green.

**Commit:** `feat(revamp): visual QA + accessibility pass`

---

## Out of scope (explicit non-goals)

- **Backend changes** — none. The API contract is unchanged. The 130 backend tests stay green.
- **Auth changes** — login flow is untouched. No OAuth, no passkeys, no 2FA in this revamp.
- **Color palette changes** — the existing accent (#2563eb), success/warning/error, and the light/dark ink scale stay. Tokens are added, not renamed.
- **Copy rewrites** — every label, heading, and CTA in the existing app stays verbatim. (Marketing copy on the landing page MAY be lightly polished, but only with explicit user review.)
- **Design-system page rewrite** — only the chrome around existing sections; no new sections unless requested.
- **Magic UI registry install** — actually in scope per D2. Use `pnpm dlx shadcn@latest add https://magicui.design/r/<name>.json` to pull each component. Customize each generated file in-place.

---

## Risk register

| Risk | Mitigation |
|------|------------|
| shadcn primitives fight the existing design system (different border radius, spacing) | Pick shadcn defaults that match the existing `rounded-md` / `rounded-lg` / `h-10` patterns. Avoid `rounded-xl` (we don't use it elsewhere). |
| `framer-motion` bundle bloat | Import only the components used (not the entire `motion` namespace). Tree-shaking handles the rest. Measure bundle with `next build` output and confirm +gzip delta < 30kb. |
| Magic UI vendor drift | Each vendored file has an attribution comment with the upstream URL + commit SHA. Pin a commit SHA in the comment so future re-vendors are deterministic. |
| Existing `8c073f9` theme toggle breaks under new shell | Verify in Phase 2 that the toggle still works from the sidebar footer; the existing AnimatedThemeToggler component is unchanged. |
| Tailwind v4 `@theme` footgun strikes again | Every new color token added in Phase 0 must go in the unprefixed `@theme` block ONLY. NO `@media (prefers-color-scheme: dark) @theme { }` blocks. Verified by inspecting emitted CSS for `:root, :host` rules. |
| Dev container serves stale CSS after rebuild | `docker compose build --no-cache frontend && docker compose up -d --force-recreate --no-deps frontend`. Don't trust `--force-recreate` alone. |
| `.NET 10 test host flake` (Internal CLR error 0x80131506) during pre-commit | Transient. Retry. If persistent, run pre-commit pieces individually. |
| `font-display: swap` causes a flash of fallback serif on first paint | Acceptable. The fallback is the system serif (`ui-serif, Georgia`) which is a reasonable default; the swap to Newsreader is fast. |

---

## Execution

This plan has 7 phases, ~20+ commits, ~50+ files touched, ~5 days of focused work.

I'll execute with `delegate_task` (subagent per phase, with two-stage review per the subagent-driven-development pattern). Each phase ends with a real browser verification + screenshot DM to you before I commit + push. Pushes go to `feat/frontend-revamp` branch (D8). You approve the final merge to `main` after Phase 7.

**One more decision I need before I start:**

## Awaiting your sign-off on:

- ~~**D10.** Order of phases / checkpoint frequency~~ → RESOLVED: end-to-end, no mid-stream checkpoints. I pause after all phases are live for your review.
- ~~**D11.** Hero headline copy~~ → RESOLVED: I decide. Locking in "Cloud, on your terms." + "Provision, monitor, and tear down cloud resources in 60 seconds. Pay only for what you use." — will revisit if a better fit surfaces mid-build.