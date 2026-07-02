/**
 * Framer Motion variants + ease curves — the shared motion vocabulary for
 * Pico. All page-level transitions and list reveals should import from
 * here so the timing is consistent across the app.
 *
 * Motion philosophy (per plan D5): intentional, not theatrical. Every
 * transition has a reason (feedback, hierarchy, delight). No bouncing /
 * wiggling on critical-path controls. Micro-interactions on hover
 * (MagicCard spotlight), on click (ShimmerButton ripple), on data change
 * (NumberTicker spring tween). Page transitions = fade + 4px upward
 * translate, 240ms ease-out. List reveals = staggered 60ms fade-in.
 */
import type { Transition, Variants } from "framer-motion";

/** "Standard" easing — ease-out for entering elements. */
export const easeStandard: Transition["ease"] = [0.2, 0, 0, 1];

/** "Emphasized" easing — slightly more deceleration for big moves. */
export const easeEmphasized: Transition["ease"] = [0.3, 0, 0, 1];

/** "Decelerate" easing — for elements that arrive with momentum. */
export const easeDecelerate: Transition["ease"] = [0, 0, 0, 1];

/** Standard duration in seconds — for framer-motion's `transition.duration`. */
export const durationStandard = 0.24;

/** Fade + 4px upward translate on enter. */
export const fadeUp: Variants = {
  hidden: { opacity: 0, y: 4 },
  visible: {
    opacity: 1,
    y: 0,
    transition: { duration: durationStandard, ease: easeStandard },
  },
};

/** Pure fade — for overlays, modals, sheets. */
export const fadeIn: Variants = {
  hidden: { opacity: 0 },
  visible: {
    opacity: 1,
    transition: { duration: 0.2, ease: easeStandard },
  },
};

/** Scale-in (0.96 → 1) with fade — for popovers, dropdowns, toasts. */
export const popIn: Variants = {
  hidden: { opacity: 0, scale: 0.96 },
  visible: {
    opacity: 1,
    scale: 1,
    transition: { duration: 0.18, ease: easeEmphasized },
  },
};

/** Container that staggers its children's reveal. Use with `variants={fadeUp}` on children. */
export const staggerContainer: Variants = {
  hidden: {},
  visible: {
    transition: { staggerChildren: 0.06, delayChildren: 0.04 },
  },
};

/** Slide up from bottom — for mobile sheets. */
export const slideUp: Variants = {
  hidden: { y: "100%" },
  visible: {
    y: 0,
    transition: { duration: 0.32, ease: easeEmphasized },
  },
};