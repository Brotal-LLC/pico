import { HTMLAttributes } from "react";
import { cn } from "@/lib/utils";

interface EmptyStateProps extends HTMLAttributes<HTMLDivElement> {
  title: string;
  description?: string;
  action?: React.ReactNode;
  icon?: React.ReactNode;
}

export function EmptyState({ title, description, action, icon, className, ...props }: EmptyStateProps) {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center py-12 px-4 text-center border border-dashed border-border rounded-lg",
        className
      )}
      {...props}
    >
      {icon && <div className="mb-4 text-muted-foreground">{icon}</div>}
      <h3 className="text-base font-semibold tracking-tight">{title}</h3>
      {description && (
        <p className="mt-1 text-sm text-muted-foreground max-w-md">{description}</p>
      )}
      {action && <div className="mt-6">{action}</div>}
    </div>
  );
}