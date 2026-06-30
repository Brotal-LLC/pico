import { HTMLAttributes } from "react";
import { cn } from "@/lib/utils";

interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: "default" | "success" | "warning" | "error" | "info";
}

const variantClasses = {
  default: "bg-muted text-foreground border-border",
  success: "bg-success/10 text-success border-success/30",
  warning: "bg-warning/10 text-warning border-warning/30",
  error: "bg-error/10 text-error border-error/30",
  info: "bg-accent/10 text-accent border-accent/30",
};

export function Badge({ variant = "default", className, ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-xs font-medium",
        variantClasses[variant],
        className
      )}
      {...props}
    />
  );
}

const statusVariant: Record<string, BadgeProps["variant"]> = {
  Created: "default",
  Provisioning: "info",
  Running: "success",
  Stopped: "warning",
  Terminated: "default",
  Failed: "error",
};

export function StatusBadge({ status }: { status: string }) {
  return <Badge variant={statusVariant[status] ?? "default"}>{status}</Badge>;
}

const invoiceStatusVariant: Record<string, BadgeProps["variant"]> = {
  Pending: "warning",
  Paid: "success",
};

export function InvoiceStatusBadge({ status }: { status: string }) {
  return <Badge variant={invoiceStatusVariant[status] ?? "default"}>{status}</Badge>;
}