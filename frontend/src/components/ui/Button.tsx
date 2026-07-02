import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "@/lib/utils";
import {
  ButtonHTMLAttributes,
  cloneElement,
  forwardRef,
  isValidElement,
  type ReactElement,
} from "react";

const buttonStyles = cva(
  "inline-flex items-center justify-center gap-2 font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2 focus-visible:ring-offset-background",
  {
    variants: {
      variant: {
        primary:
          "bg-primary text-primary-foreground hover:bg-primary/90 border border-primary",
        secondary:
          "bg-muted text-foreground hover:bg-muted/80 border border-border",
        outline:
          "bg-transparent text-foreground hover:bg-muted border border-border",
        ghost: "bg-transparent text-foreground hover:bg-muted",
        danger:
          "bg-transparent text-error hover:bg-error/10 border border-error",
        // New variants for the SaaS revamp — wire in Phase 4 (landing page).
        shimmer: "bg-primary text-primary-foreground border border-primary",
        link: "bg-transparent text-accent hover:underline underline-offset-4 border-transparent",
        gradient: "bg-gradient-to-r from-accent to-accent/70 text-accent-foreground border-0",
      },
      size: {
        sm: "h-8 px-3 text-sm rounded-md",
        md: "h-10 px-4 text-sm rounded-md",
        lg: "h-12 px-6 text-base rounded-lg",
        icon: "h-10 w-10 rounded-md",
        // New: compact icon size for in-table use (36px square — large
        // enough to hit, small enough not to dominate row height).
        "icon-sm": "h-9 w-9 rounded-md",
      },
    },
    defaultVariants: { variant: "primary", size: "md" },
  }
);

/**
 * Compose a button class string for non-`<button>` elements (e.g. the
 * animated theme toggler primitive that renders its own button).
 */
export function buttonClass(
  ...inputs: Parameters<typeof buttonStyles>
): string {
  return buttonStyles(...inputs);
}

export interface ButtonProps
  extends ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonStyles> {
  asChild?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, children, ...props }, ref) => {
    const classes = cn(buttonStyles({ variant, size }), "touch-target", className);

    if (asChild) {
      if (!isValidElement(children)) return null;

      const child = children as ReactElement<{ className?: string }>;
      return cloneElement(child, {
        ...props,
        ref,
        className: cn(classes, child.props.className),
      } as React.HTMLAttributes<HTMLElement> & { ref: typeof ref });
    }

    return (
      <button ref={ref} className={classes} {...props}>
        {children}
      </button>
    );
  }
);
Button.displayName = "Button";
