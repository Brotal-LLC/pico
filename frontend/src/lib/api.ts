/**
 * Type-safe API client. Throws PicoApiError on non-2xx so React Query catches them.
 * Includes CSRF-safe credentials (cookies) for session auth.
 */

// Public URL the browser uses to reach the API (set at build time via NEXT_PUBLIC_API_URL)
const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

export class PicoApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly statusText: string,
    public readonly body: unknown,
    message: string
  ) {
    super(message);
    this.name = "PicoApiError";
  }
}

export class UnauthenticatedError extends PicoApiError {
  constructor(statusText: string, body: unknown) {
    super(401, statusText, body, "Not authenticated");
    this.name = "UnauthenticatedError";
  }
}

interface ApiOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
  body?: unknown;
  signal?: AbortSignal;
  headers?: Record<string, string>;
}

async function request<T>(path: string, opts: ApiOptions = {}): Promise<T> {
  const { method = "GET", body, signal, headers = {} } = opts;
  const init: RequestInit = {
    method,
    credentials: "include",
    signal,
    headers: {
      "Content-Type": "application/json",
      ...headers,
    },
  };
  if (body !== undefined) {
    init.body = JSON.stringify(body);
  }
  const res = await fetch(`${API_URL}${path}`, init);
  if (!res.ok) {
    const errBody = await res.json().catch(() => ({}));
    if (res.status === 401) {
      throw new UnauthenticatedError(res.statusText, errBody);
    }
    throw new PicoApiError(
      res.status,
      res.statusText,
      errBody,
      errBody?.error ?? res.statusText
    );
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

// ─── Catalog types ─────────────────────────────────────────────────────────
export interface Flavor {
  id: string;
  name: string;
  vcpus: number;
  ramMb: number;
  diskGb: number;
  pricePerHour: number;
  pricePerMonth: number;
  category: string;
}

export interface OsImage {
  id: string;
  name: string;
  os: string;
  version: string;
  sizeGb: number;
}

// ─── Auth types ────────────────────────────────────────────────────────────
export interface AuthUser {
  id: string;
  email: string;
  name: string;
  role: "Customer" | "Admin";
}

export const auth = {
  me: () => request<AuthUser>("/api/auth/me"),
  signup: (data: { email: string; name: string; password: string }) =>
    request<AuthUser>("/api/auth/signup", { method: "POST", body: data }),
  login: (data: { email: string; password: string }) =>
    request<AuthUser>("/api/auth/login", { method: "POST", body: data }),
  logout: () => request<{ ok: boolean }>("/api/auth/logout", { method: "POST" }),
};

// ─── Catalog ──────────────────────────────────────────────────────────────
export const catalog = {
  flavors: () => request<Flavor[]>("/api/catalog/flavors"),
  flavor: (id: string) => request<Flavor>(`/api/catalog/flavors/${id}`),
  images: () => request<OsImage[]>("/api/catalog/images"),
};

// ─── Resources ────────────────────────────────────────────────────────────
export interface ResourceSummary {
  id: string;
  name: string;
  status: string;
  flavorId: string;
  imageId: string;
  ipAddress: string | null;
  externalId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ResourceEvent {
  id: string;
  type: string;
  oldStatus: string;
  newStatus: string;
  message: string;
  timestamp: string;
}

export interface ResourceDetail extends ResourceSummary {
  events: ResourceEvent[];
}

export interface ResourceUsage {
  cpuPercent: number;
  ramMbUsed: number;
  diskIoKbps: number;
  networkBytesIn: number;
  networkBytesOut: number;
  sampledAt: string;
}

export const resources = {
  list: () => request<ResourceSummary[]>("/api/resources"),
  detail: (id: string) => request<ResourceDetail>(`/api/resources/${id}`),
  provision: (data: { name: string; flavorId: string; imageId: string }) =>
    request<ResourceSummary>("/api/resources", { method: "POST", body: data }),
  start: (id: string) =>
    request<ResourceSummary>(`/api/resources/${id}/start`, { method: "POST", body: {} }),
  stop: (id: string) =>
    request<ResourceSummary>(`/api/resources/${id}/stop`, { method: "POST", body: {} }),
  terminate: (id: string) =>
    request<ResourceSummary>(`/api/resources/${id}`, { method: "DELETE" }),
  usage: (id: string) => request<ResourceUsage>(`/api/resources/${id}/usage`),
  eventsUrl: (id: string) => `${API_URL}/api/resources/${id}/events`,
};

// ─── Invoices ────────────────────────────────────────────────────────────
export interface InvoiceListItem {
  id: string;
  userId: string;
  periodStart: string;
  periodEnd: string;
  total: number;
  status: string;
  createdAt: string;
  paidAt: string | null;
  lineCount: number;
}

export interface InvoiceLine {
  id: string;
  resourceId: string;
  flavorId: string;
  hours: number;
  rate: number;
  amount: number;
  description: string;
}

export interface InvoiceDetail extends InvoiceListItem {
  lines: InvoiceLine[];
}

export const invoices = {
  list: () => request<InvoiceListItem[]>("/api/invoices"),
  detail: (id: string) => request<InvoiceDetail>(`/api/invoices/${id}`),
  pay: (id: string) =>
    request<{ ok: boolean; status: string }>(`/api/invoices/${id}/pay`, {
      method: "POST",
      body: {},
    }),
};

// ─── Admin ───────────────────────────────────────────────────────────────
export interface AdminMetrics {
  totalUsers: number;
  totalResources: number;
  activeResources: number;
  terminatedResources: number;
  failedResources: number;
  totalInvoices: number;
  paidInvoices: number;
  pendingInvoices: number;
  totalRevenue: number;
}

export interface AdminUser {
  id: string;
  email: string;
  name: string;
  role: string;
  createdAt: string;
}

export const admin = {
  metrics: () => request<AdminMetrics>("/api/admin/metrics"),
  users: () => request<AdminUser[]>("/api/admin/users"),
  resources: () => request<ResourceSummary[]>("/api/admin/resources"),
};

// ─── Health ─────────────────────────────────────────────────────────────
export interface Health {
  status: string;
  backend: string;
  backendHealthy: boolean;
  timestamp: string;
}

export const health = {
  get: () => request<Health>("/api/health"),
};

export { API_URL };