"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { auth, AuthUser, UnauthenticatedError } from "@/lib/api";

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  error: string | null;
  login: (email: string, password: string) => Promise<void>;
  signup: (email: string, name: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refresh: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Routes that do NOT require authentication. Mounting AuthProvider at the
 * root layout means `refresh()` runs on every page load — but on public
 * pages that call no protected data, the resulting 401 just pollutes the
 * browser console. We skip the network probe on these paths only on the
 * initial mount. Subsequent navigations keep the existing user state so
 * transitions like `/dashboard` → `/catalog` don't flicker.
 */
const PUBLIC_ROUTES = new Set<string>(["/", "/login", "/signup", "/catalog"]);

function isPublicRoute(pathname: string | null): boolean {
  if (!pathname) return false;
  if (PUBLIC_ROUTES.has(pathname)) return true;
  // The (dashboard) variants are protected and live under a different segment
  // — only the public `/catalog` (root segment) is open.
  return false;
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const pathname = usePathname();
  const initialised = useRef(false);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const me = await auth.me();
      setUser(me);
    } catch (err) {
      if (err instanceof UnauthenticatedError) {
        setUser(null);
      } else {
        setError(err instanceof Error ? err.message : "Auth failed");
        setUser(null);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    setError(null);
    const u = await auth.login({ email, password });
    setUser(u);
  }, []);

  const signup = useCallback(async (email: string, name: string, password: string) => {
    setError(null);
    const u = await auth.signup({ email, name, password });
    setUser(u);
  }, []);

  const logout = useCallback(async () => {
    try {
      await auth.logout();
    } finally {
      setUser(null);
    }
  }, []);

  useEffect(() => {
    // Only the *initial* mount avoids the me probe on public pages. After
    // that the cached user/loading state is authoritative for the lifetime
    // of the provider. The pathname dependency is captured at mount time
    // deliberately — subsequent navigations reuse the cached state.
    if (initialised.current) return;
    initialised.current = true;

    if (isPublicRoute(pathname)) {
      setUser(null);
      setLoading(false);
      return;
    }
    void refresh();
  }, [pathname, refresh]);

  const value = useMemo(
    () => ({ user, loading, error, login, signup, logout, refresh }),
    [user, loading, error, login, signup, logout, refresh]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within <AuthProvider>");
  return ctx;
}