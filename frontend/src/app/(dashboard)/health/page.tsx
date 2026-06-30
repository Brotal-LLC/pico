"use client";

import { useQuery } from "@tanstack/react-query";
import { health } from "@/lib/api";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { PageSpinner } from "@/components/ui/Spinner";
import { formatRelativeTime, getErrorMessage } from "@/lib/utils";

export default function HealthPage() {
  const { data, isLoading, isError, error, dataUpdatedAt } = useQuery({
    queryKey: ["health"],
    queryFn: () => health.get(),
    refetchInterval: 10000,
  });

  if (isLoading) return <PageSpinner />;

  if (isError || !data) {
    return (
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Health</h1>
          <p className="text-sm text-muted-foreground mt-1">Service status</p>
        </div>
        <Card>
          <CardBody>
            <p className="text-sm text-error">{getErrorMessage(error, "Unable to load health status")}</p>
          </CardBody>
        </Card>
      </div>
    );
  }

  const apiHealthy = data.status.toLowerCase() === "ok" || data.status.toLowerCase() === "healthy";

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Health</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Service status
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Backend</CardTitle>
        </CardHeader>
        <CardBody>
          <StatusRow
            label="API"
            value={data.status}
            detail={formatRelativeTime(data.timestamp)}
            ok={apiHealthy}
          />
          <StatusRow
            label="Provisioning backend"
            value={data.backend}
            detail={data.backendHealthy ? "Healthy" : "Unhealthy"}
            ok={data.backendHealthy}
          />
        </CardBody>
      </Card>

      <p className="text-xs text-muted-foreground">
        Auto-refreshes every 10 seconds. Last fetched {formatRelativeTime(new Date(dataUpdatedAt))}.
      </p>
    </div>
  );
}

function StatusRow({ label, value, detail, ok }: { label: string; value: string; detail: string; ok: boolean }) {
  return (
    <div className="flex items-center justify-between py-2 border-b border-border last:border-0">
      <span className="text-sm text-muted-foreground">{label}</span>
      <div className="text-right">
        <div className="font-mono text-sm font-medium">
          <span className={ok ? "text-success" : "text-error"}>● {value}</span>
        </div>
        <div className="text-xs text-muted-foreground mt-0.5">{detail}</div>
      </div>
    </div>
  );
}
