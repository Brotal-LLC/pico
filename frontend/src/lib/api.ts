/**
 * Type-safe API client. Throws PicoApiError on non-2xx so React Query catches them.
 * Includes CSRF-safe credentials (cookies) for session auth.
 */

// Public URL the browser uses to reach the API (set at build time via NEXT_PUBLIC_API_URL)
// Server-side uses API_URL (internal Docker network) when available
const API_URL = typeof window === "undefined"
  ? process.env.API_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080"
  : process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

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

let csrfToken: string | null = null;
let csrfTokenPromise: Promise<string | null> | null = null;

function isUnsafeMethod(method: string) {
  return method !== "GET" && method !== "HEAD" && method !== "OPTIONS";
}

function getErrorMessage(body: unknown, fallback: string) {
  if (body && typeof body === "object" && "error" in body) {
    const error = (body as { error?: unknown }).error;
    if (typeof error === "string") return error;
  }
  return fallback;
}

async function fetchCsrfToken(): Promise<string | null> {
  if (csrfToken) return csrfToken;

  csrfTokenPromise ??= fetch(`${API_URL}/api/auth/csrf-token`, {
    credentials: "include",
  })
    .then(async (res) => {
      if (!res.ok) return null;
      const body = await res.json().catch(() => ({}));
      return typeof body?.token === "string" ? body.token : null;
    })
    .catch(() => null)
    .finally(() => {
      csrfTokenPromise = null;
    });

  csrfToken = await csrfTokenPromise;
  return csrfToken;
}

async function request<T>(path: string, opts: ApiOptions = {}): Promise<T> {
  const { method = "GET", body, signal, headers = {} } = opts;
  const unsafe = isUnsafeMethod(method);

  const makeRequest = async (token: string | null) => {
    const requestHeaders: Record<string, string> = {
      "Content-Type": "application/json",
      ...headers,
    };

    if (unsafe && token) {
      requestHeaders["X-CSRF-TOKEN"] = token;
    }

    const init: RequestInit = {
      method,
      credentials: "include",
      signal,
      headers: requestHeaders,
    };

    if (body !== undefined) {
      init.body = JSON.stringify(body);
    }

    return fetch(`${API_URL}${path}`, init);
  };

  const token = unsafe ? await fetchCsrfToken() : null;
  let res = await makeRequest(token);

  if (unsafe && res.status === 403 && token) {
    csrfToken = null;
    res = await makeRequest(await fetchCsrfToken());
  }

  if (!res.ok) {
    const errBody = await res.json().catch(() => ({}));
    if (res.status === 401) {
      throw new UnauthenticatedError(res.statusText, errBody);
    }
    throw new PicoApiError(
      res.status,
      res.statusText,
      errBody,
      getErrorMessage(errBody, res.statusText)
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
  /**
   * Terraform-style "what will happen if I commit?" preview. Does NOT
   * create a resource. Returns monthly/hourly cost + disk-fit + warnings.
   */
  preview: (data: { flavorId: string; imageId: string }) =>
    request<ProvisioningPlan>("/api/resources/preview", {
      method: "POST",
      body: { name: "preview", ...data },
    }),
  start: (id: string) =>
    request<ResourceSummary>(`/api/resources/${id}/start`, { method: "POST", body: {} }),
  stop: (id: string) =>
    request<ResourceSummary>(`/api/resources/${id}/stop`, { method: "POST", body: {} }),
  terminate: (id: string) =>
    request<ResourceSummary>(`/api/resources/${id}`, { method: "DELETE" }),
  usage: (id: string) => request<ResourceUsage>(`/api/resources/${id}/usage`),
  eventsUrl: (id: string) => `${API_URL}/api/resources/${id}/events`,
};

export interface ProvisioningPlan {
  monthlyCostEstimate: number;
  hourlyCostEstimate: number;
  vcpus: number;
  ramMb: number;
  diskGb: number;
  imageName: string;
  imageOs: string;
  imageVersion: string;
  imageSizeGb: number;
  imageFitsInFlavorDisk: boolean;
  warnings: string[];
}

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
export interface ResourceSlaSummary {
  running: number;
  stopped: number;
  provisioning: number;
  failed: number;
  terminated: number;
  totalUptimeHours: number;
  totalPossibleUptimeHours: number;
  uptimePercent: number;
}

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
  fleetUptimePercent: number;
  resourcesOlderThan24h: number;
  oldestActiveResourceAt: string | null;
  sla: ResourceSlaSummary;
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
