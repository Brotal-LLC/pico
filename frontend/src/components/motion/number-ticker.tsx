"use client";

/**
 * NumberTicker — a <span> whose text content tweens between numeric
 * values via a framer-motion spring. Used for dashboard stats (uptime %,
 * active resource count, monthly spend) where the value updates over
 * time and we want a smooth visual transition rather than a snap.
 *
 * CRITICAL: this is a re-animating variant. The off-the-shelf MagicUI
 * NumberTicker uses `useInView({ once: true })` and only animates on the
 * first scroll into view. For stat counters that update every few
 * seconds (e.g. CPU %, live request count), the off-the-shelf component
 * silently fails to animate after the first value. We rebuild it with a
 * `useEffect` that resets the previous value on every prop change.
 *
 * Respects `prefers-reduced-motion`: when on, renders the final value
 * immediately without animation.
 */
import {
  motion,
  useMotionValue,
  useReducedMotion,
  useSpring,
} from "framer-motion";
import { useEffect, useRef } from "react";

interface NumberTickerProps {
  value: number;
  /** Decimal places. Default 0. */
  decimalPlaces?: number;
  /** Optional locale. Default "en-US". */
  locale?: string;
  /** Optional Intl.NumberFormat options override (e.g. currency style). */
  formatOptions?: Intl.NumberFormatOptions;
  /** Spring stiffness. Default 220. */
  stiffness?: number;
  /** Spring damping. Default 26. */
  damping?: number;
  /** Class names for the wrapping <span>. */
  className?: string;
  /** Inline style for the wrapping <span>. */
  style?: React.CSSProperties;
  /** Element to render. Default "span". */
  as?: "span" | "div" | "p";
}

export function NumberTicker({
  value,
  decimalPlaces = 0,
  locale = "en-US",
  formatOptions,
  stiffness = 220,
  damping = 26,
  className,
  style,
  as = "span",
}: NumberTickerProps) {
  const ref = useRef<HTMLSpanElement>(null);
  const previousValue = useRef<number>(value);
  const motionValue = useMotionValue(value);
  const spring = useSpring(motionValue, { stiffness, damping });
  const reduce = useReducedMotion();

  // When `value` changes, reset the spring's resting point to the previous
  // value, then jump to the new value in the next frame. This forces a
  // tween every time the parent updates, not just the first.
  useEffect(() => {
    if (reduce) {
      motionValue.set(value);
      previousValue.current = value;
      return;
    }
    motionValue.set(previousValue.current);
    const id = requestAnimationFrame(() => motionValue.set(value));
    previousValue.current = value;
    return () => cancelAnimationFrame(id);
  }, [value, motionValue, reduce]);

  // Pipe the spring's latest value into the DOM as formatted text.
  useEffect(() => {
    const formatter = new Intl.NumberFormat(locale, {
      minimumFractionDigits: decimalPlaces,
      maximumFractionDigits: decimalPlaces,
      ...formatOptions,
    });
    return spring.on("change", (latest) => {
      if (ref.current) {
        ref.current.textContent = formatter.format(Number(latest.toFixed(decimalPlaces)));
      }
    });
  }, [spring, locale, decimalPlaces, formatOptions]);

  // Initial render — show the raw value (no animation needed for first paint).
  const formatter = new Intl.NumberFormat(locale, {
    minimumFractionDigits: decimalPlaces,
    maximumFractionDigits: decimalPlaces,
    ...formatOptions,
  });

  const MotionTag = motion[as] as typeof motion.span;

  return (
    <MotionTag
      ref={ref}
      className={className}
      style={style}
      // tabular-nums utility — keeps the digit widths consistent during
      // the tween so the column doesn't jitter.
    >
      {formatter.format(value)}
    </MotionTag>
  );
}