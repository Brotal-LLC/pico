"use client";

import { useQuery } from "@tanstack/react-query";
import { health, Health } from "@/lib/api";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { PageSpinner } from "@/components/ui/Spinner";
import { formatRelativeTime } from "@/lib/utils";

export default function HealthPage() {
  const { data, isLoading, refetch } = useQuery({
    queryKey: ["health"],
    queryFn: () => health.get(),
    refetchInterval: 10000,
  });

  if (isLoading || !data) return <PageSpinner />;

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
            value="ok"
            detail={formatRelativeTime(data.timestamp)}
            ok
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
        Auto-refreshes every 10 seconds. Last fetched {formatRelativeTime(new Date())}.
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
          {ok ? "●" : "●"} <span className={ok ? "text-success" : "text-error"}>{value}</span>
        </div>
        <div className="text-xs text-muted-foreground mt-0.5">{detail}</div>
      </div>
    </div>
  );
}