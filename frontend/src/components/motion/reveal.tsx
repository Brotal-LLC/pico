"use client";

/**
 * Reveal — wraps content in a framer-motion <motion.div> that fades +
 * translates the FIRST time it scrolls into view. Useful for hero blocks,
 * section titles, "below the fold" marketing blocks. Does NOT animate on
 * subsequent visits (viewport once: true).
 *
 * Respects `prefers-reduced-motion` — when on, children render at their
 * final state immediately.
 *
 * Usage:
 *   <Reveal>
 *     <h1>...</h1>
 *   </Reveal>
 *
 * Or for staggered lists, set `stagger` to true on the parent and
 * `as="child"` on each child Reveal. (The current shape only supports
 * the standalone use; staggered lists should import `staggerContainer`
 * and `fadeUp` from `./variants` directly.)
 */
import { motion, useInView, useReducedMotion } from "framer-motion";
import { useRef, type ReactNode } from "react";
import { easeStandard } from "./variants";

interface RevealProps {
  children: ReactNode;
  /** Distance to translate from, in pixels. Default 8px. */
  distance?: number;
  /** Delay before the reveal starts (seconds). Default 0. */
  delay?: number;
  /** How much of the element must be visible to trigger. Default 0.2. */
  amount?: number;
  /** Render as a different element. Default "div". */
  as?: "div" | "section" | "article" | "li";
  /** Class names for the wrapping element. */
  className?: string;
}

export function Reveal({
  children,
  distance = 8,
  delay = 0,
  amount = 0.2,
  as = "div",
  className,
}: RevealProps) {
  const ref = useRef<HTMLDivElement>(null);
  const inView = useInView(ref, { once: true, amount });
  const reduce = useReducedMotion();

  const MotionTag = motion[as] as typeof motion.div;

  return (
    <MotionTag
      ref={ref}
      className={className}
      initial={reduce ? { opacity: 1 } : { opacity: 0, y: distance }}
      animate={inView || reduce ? { opacity: 1, y: 0 } : undefined}
      transition={{ duration: 0.32, ease: easeStandard, delay }}
    >
      {children}
    </MotionTag>
  );
}