"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/components/AuthProvider";
import { LogOut, Cloud, LayoutDashboard, Receipt, Activity, Shield } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { cn } from "@/lib/utils";

export function Sidebar() {
  const pathname = usePathname();
  const { user, logout } = useAuth();

  const nav = [
    { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
    { href: "/catalog", label: "Catalog", icon: Cloud },
    { href: "/billing", label: "Billing", icon: Receipt },
    { href: "/health", label: "Health", icon: Activity },
  ];

  if (user?.role === "Admin") {
    nav.push({ href: "/admin", label: "Admin", icon: Shield });
  }

  return (
    <aside className="w-60 border-r border-border bg-background flex flex-col h-screen sticky top-0">
      <div className="px-6 py-5 border-b border-border">
        <Link href="/dashboard" className="text-xl font-bold tracking-tight">Pico</Link>
        <p className="text-xs text-muted-foreground mt-0.5">Self-service cloud</p>
      </div>

      <nav className="flex-1 px-3 py-4 space-y-1">
        {nav.map((item) => {
          const Icon = item.icon;
          const active = pathname?.startsWith(item.href);
          return (
            <Link
              key={item.href}
              href={item.href}
              className={cn(
                "flex items-center gap-3 px-3 py-2 rounded-md text-sm transition-colors",
                active
                  ? "bg-muted text-foreground font-medium"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground"
              )}
            >
              <Icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}
      </nav>

      <div className="px-3 py-4 border-t border-border">
        <div className="px-3 mb-3">
          <p className="text-sm font-medium truncate">{user?.name}</p>
          <p className="text-xs text-muted-foreground truncate">{user?.email}</p>
          <p className="text-xs text-muted-foreground mt-1">
            <span className="font-mono text-xs">{user?.role}</span>
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          className="w-full justify-start"
          onClick={() => logout()}
        >
          <LogOut className="h-4 w-4" />
          Sign out
        </Button>
      </div>
    </aside>
  );
}