"use client";

import { useQuery } from "@tanstack/react-query";
import { admin } from "@/lib/api";
import { Card, CardBody, CardHeader, CardTitle, CardDescription } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { PageSpinner } from "@/components/ui/Spinner";
import { formatCurrency, formatRelativeTime, getErrorMessage } from "@/lib/utils";
import { usePageTitle } from "@/lib/use-page-title";

export default function AdminPage() {
  usePageTitle("Admin");
  const {
    data: metrics,
    isLoading: metricsLoading,
    isError: metricsIsError,
    error: metricsError,
  } = useQuery({
    queryKey: ["admin", "metrics"],
    queryFn: () => admin.metrics(),
    refetchInterval: 15000,
  });
  const {
    data: users,
    isLoading: usersLoading,
    isError: usersIsError,
    error: usersError,
  } = useQuery({
    queryKey: ["admin", "users"],
    queryFn: () => admin.users(),
  });

  if (metricsLoading) return <PageSpinner />;

  if (metricsIsError || !metrics) {
    return (
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Admin</h1>
          <p className="text-sm text-muted-foreground mt-1">Operational metrics and user directory</p>
        </div>
        <Card>
          <CardBody>
            <p className="text-sm text-error">{getErrorMessage(metricsError, "Unable to load admin metrics")}</p>
          </CardBody>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Admin</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Operational metrics and user directory
        </p>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-4">
        <Metric label="Total users" value={metrics.totalUsers} />
        <Metric label="Resources" value={metrics.totalResources} detail={`${metrics.activeResources} active`} />
        <Metric label="Terminated" value={metrics.terminatedResources} />
        <Metric label="Failed" value={metrics.failedResources} />
        <Metric label="Invoices" value={metrics.totalInvoices} detail={`${metrics.paidInvoices} paid`} />
        <Metric label="Revenue" value={formatCurrency(metrics.totalRevenue)} />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Users</CardTitle>
          <CardDescription className="mt-1">All registered users</CardDescription>
        </CardHeader>
        <CardBody className="p-0">
          {usersLoading ? (
            <div className="p-6">
              <p className="text-sm text-muted-foreground">Loading users…</p>
            </div>
          ) : usersIsError ? (
            <div className="p-6">
              <p className="text-sm text-error">{getErrorMessage(usersError, "Unable to load users")}</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[640px] text-sm">
                <thead>
                  <tr className="border-b border-border text-muted-foreground">
                    <th className="px-6 py-3 text-left font-medium">Name</th>
                    <th className="px-6 py-3 text-left font-medium">Email</th>
                    <th className="px-6 py-3 text-left font-medium">Role</th>
                    <th className="px-6 py-3 text-left font-medium">Joined</th>
                  </tr>
                </thead>
                <tbody>
                  {users?.map((u) => (
                    <tr key={u.id} className="border-b border-border last:border-0">
                      <td className="px-6 py-3 font-medium">{u.name}</td>
                      <td className="px-6 py-3 text-muted-foreground">{u.email}</td>
                      <td className="px-6 py-3">
                        <Badge variant={u.role === "Admin" ? "info" : "default"}>{u.role}</Badge>
                      </td>
                      <td className="px-6 py-3 text-muted-foreground">{formatRelativeTime(u.createdAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardBody>
      </Card>
    </div>
  );
}

function Metric({ label, value, detail }: { label: string; value: number | string; detail?: string }) {
  return (
    <Card>
      <CardBody>
        <div className="text-xs text-muted-foreground uppercase tracking-wide">{label}</div>
        <div className="font-mono text-2xl font-bold mt-1">{value}</div>
        {detail && <div className="text-xs text-muted-foreground mt-1">{detail}</div>}
      </CardBody>
    </Card>
  );
}
