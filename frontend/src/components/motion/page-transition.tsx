"use client";

/**
 * PageTransition — wraps page content in a framer-motion <motion.div> that
 * fades + 4px upward translates on mount. Use inside a server-rendered
 * layout to avoid serializing motion config through the RSC boundary.
 *
 * Usage in a layout/page:
 *   import { PageTransition } from "@/components/motion/page-transition";
 *   ...
 *   <PageTransition>{children}</PageTransition>
 *
 * Respects `prefers-reduced-motion`: when the user has reduced-motion on,
 * the duration collapses to 0 and the y-translate is removed. The page
 * still re-renders, but without the visual motion.
 */
import { motion, useReducedMotion } from "framer-motion";
import type { ReactNode } from "react";
import { easeStandard } from "./variants";

interface PageTransitionProps {
  children: ReactNode;
  /** Override the entrance duration (seconds). Defaults to 0.24. */
  duration?: number;
}

export function PageTransition({ children, duration = 0.24 }: PageTransitionProps) {
  const reduce = useReducedMotion();
  return (
    <motion.div
      initial={reduce ? { opacity: 1 } : { opacity: 0, y: 4 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: reduce ? 0 : duration, ease: easeStandard }}
    >
      {children}
    </motion.div>
  );
}