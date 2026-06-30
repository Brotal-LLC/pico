"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
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

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
    refresh();
  }, [refresh]);

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