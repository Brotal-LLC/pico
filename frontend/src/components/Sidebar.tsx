"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { useAuth } from "@/components/AuthProvider";
import {
  LogOut,
  Cloud,
  LayoutDashboard,
  Receipt,
  Activity,
  Shield,
  Menu,
  X,
} from "lucide-react";
import { Button } from "@/components/ui/Button";
import { cn } from "@/lib/utils";

type NavItem = {
  href: string;
  label: string;
  icon: typeof LayoutDashboard;
};

export function Sidebar() {
  const pathname = usePathname();
  const { user, logout } = useAuth();
  const [open, setOpen] = useState(false);

  const nav: NavItem[] = [
    { href: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
    { href: "/catalog", label: "Catalog", icon: Cloud },
    { href: "/billing", label: "Billing", icon: Receipt },
    { href: "/health", label: "Health", icon: Activity },
  ];

  if (user?.role === "Admin") {
    nav.push({ href: "/admin", label: "Admin", icon: Shield });
  }

  return (
    <>
      <div className="md:hidden fixed inset-x-0 top-0 z-30 h-14 border-b border-border bg-background flex items-center justify-between px-4">
        <div>
          <Link href="/dashboard" className="text-xl font-bold tracking-tight" onClick={() => setOpen(false)}>
            Pico
          </Link>
          <p className="text-xs text-muted-foreground -mt-0.5">Self-service cloud</p>
        </div>
        <Button
          variant="ghost"
          size="icon"
          aria-label={open ? "Close navigation" : "Open navigation"}
          aria-expanded={open}
          onClick={() => setOpen((value) => !value)}
        >
          {open ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </Button>
      </div>

      {open && (
        <button
          type="button"
          aria-label="Close navigation"
          className="md:hidden fixed inset-0 z-20 bg-background/80 backdrop-blur-sm"
          onClick={() => setOpen(false)}
        />
      )}

      <aside
        className={cn(
          "fixed inset-y-0 left-0 z-30 w-60 border-r border-border bg-background flex flex-col h-screen transition-transform md:sticky md:top-0 md:translate-x-0",
          open ? "translate-x-0" : "-translate-x-full md:translate-x-0"
        )}
      >
        <SidebarContent
          nav={nav}
          pathname={pathname}
          user={user}
          onNavigate={() => setOpen(false)}
          onLogout={() => {
            setOpen(false);
            logout();
          }}
        />
      </aside>
    </>
  );
}

function SidebarContent({
  nav,
  pathname,
  user,
  onNavigate,
  onLogout,
}: {
  nav: NavItem[];
  pathname: string | null;
  user: ReturnType<typeof useAuth>["user"];
  onNavigate: () => void;
  onLogout: () => void;
}) {
  return (
    <>
      <div className="px-6 py-5 border-b border-border">
        <Link href="/dashboard" className="text-xl font-bold tracking-tight" onClick={onNavigate}>Pico</Link>
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
              onClick={onNavigate}
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
          <div className="flex items-center gap-2">
            <p className="text-sm font-medium truncate">{user?.name}</p>
            {user?.role === "Admin" && (
              <span className="inline-flex items-center rounded-md border border-border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
                Admin
              </span>
            )}
          </div>
          <p className="text-xs text-muted-foreground truncate">{user?.email}</p>
        </div>
        <Button
          variant="outline"
          size="sm"
          className="w-full justify-start"
          onClick={onLogout}
        >
          <LogOut className="h-4 w-4" />
          Sign out
        </Button>
      </div>
    </>
  );
}
