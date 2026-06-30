"use client";

import { use, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { toast } from "sonner";
import {
  Area, AreaChart, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid,
} from "recharts";
import { resources, ResourceDetail, ResourceEvent, ResourceUsage } from "@/lib/api";
import { Card, CardBody, CardHeader, CardTitle, CardDescription } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { StatusBadge } from "@/components/ui/Badge";
import { PageSpinner } from "@/components/ui/Spinner";
import { ArrowLeft, Play, Square, Trash2 } from "lucide-react";
import { formatBytes, formatRelativeTime } from "@/lib/utils";

export default function ResourceDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const router = useRouter();
  const qc = useQueryClient();

  const { data: detail, isLoading } = useQuery({
    queryKey: ["resource", id],
    queryFn: () => resources.detail(id),
    refetchInterval: (q) => {
      const data = q.state.data as ResourceDetail | undefined;
      // Auto-refetch while non-terminal state for live status updates
      if (data && !["Terminated", "Failed"].includes(data.status)) return 5000;
      return false;
    },
  });

  const { data: usage, refetch: refetchUsage } = useQuery({
    queryKey: ["resource-usage", id],
    queryFn: () => resources.usage(id),
    refetchInterval: 5000,
  });

  const [events, setEvents] = useState<ResourceEvent[]>([]);

  // SSE: listen for live status updates
  useEffect(() => {
    if (!id) return;
    const es = new EventSource(resources.eventsUrl(id), { withCredentials: true });
    es.onmessage = (e) => {
      try {
        const evt = JSON.parse(e.data) as ResourceEvent;
        setEvents((prev) => [...prev, evt].slice(-50));
        qc.invalidateQueries({ queryKey: ["resource", id] });
      } catch {
        // Ignore keep-alive comments
      }
    };
    es.onerror = () => {
      es.close();
    };
    return () => es.close();
  }, [id, qc]);

  const start = useMutation({
    mutationFn: () => resources.start(id),
    onSuccess: () => {
      toast.success("Resource starting");
      qc.invalidateQueries({ queryKey: ["resource", id] });
    },
    onError: (e) => toast.error(e instanceof Error ? e.message : "Start failed"),
  });
  const stop = useMutation({
    mutationFn: () => resources.stop(id),
    onSuccess: () => {
      toast.success("Resource stopping");
      qc.invalidateQueries({ queryKey: ["resource", id] });
    },
    onError: (e) => toast.error(e instanceof Error ? e.message : "Stop failed"),
  });
  const terminate = useMutation({
    mutationFn: () => resources.terminate(id),
    onSuccess: () => {
      toast.success("Resource terminated");
      qc.invalidateQueries({ queryKey: ["resources"] });
      qc.invalidateQueries({ queryKey: ["resource", id] });
      router.push("/dashboard");
    },
    onError: (e) => toast.error(e instanceof Error ? e.message : "Terminate failed"),
  });

  if (isLoading || !detail) return <PageSpinner />;

  const allEvents = [...(detail.events ?? []), ...events];
  const isRunning = detail.status === "Running";
  const isStopped = detail.status === "Stopped";
  const isProvisioning = detail.status === "Provisioning" || detail.status === "Created";
  const isTerminal = detail.status === "Terminated" || detail.status === "Failed";

  // Build usage sparkline data — we have one point per fetch; show last 12
  const usageData = (usage
    ? [{ time: 0, cpu: usage.cpuPercent, ram: usage.ramMbUsed }]
    : []);

  return (
    <div className="space-y-6">
      <Link href="/dashboard" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
        <ArrowLeft className="h-3 w-3" />
        Back to dashboard
      </Link>

      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">{detail.name}</h1>
          <div className="flex items-center gap-2 mt-2">
            <StatusBadge status={detail.status} />
            {detail.ipAddress && (
              <span className="text-sm font-mono text-muted-foreground">{detail.ipAddress}</span>
            )}
          </div>
        </div>
        <div className="flex gap-2">
          {!isTerminal && (
            <>
              <Button
                variant="outline"
                onClick={() => start.mutate()}
                disabled={start.isPending || isRunning || isProvisioning}
              >
                <Play className="h-4 w-4" />
                Start
              </Button>
              <Button
                variant="outline"
                onClick={() => stop.mutate()}
                disabled={stop.isPending || !isRunning}
              >
                <Square className="h-4 w-4" />
                Stop
              </Button>
              <Button
                variant="danger"
                onClick={() => terminate.mutate()}
                disabled={terminate.isPending}
              >
                <Trash2 className="h-4 w-4" />
                Terminate
              </Button>
            </>
          )}
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Card>
          <CardHeader><CardTitle>CPU</CardTitle></CardHeader>
          <CardBody>
            <div className="font-mono text-3xl font-bold">
              {usage?.cpuPercent.toFixed(1) ?? "—"}
              <span className="text-base text-muted-foreground ml-1">%</span>
            </div>
            <p className="text-xs text-muted-foreground mt-1">
              {usage ? formatRelativeTime(usage.sampledAt) : "—"}
            </p>
          </CardBody>
        </Card>
        <Card>
          <CardHeader><CardTitle>RAM</CardTitle></CardHeader>
          <CardBody>
            <div className="font-mono text-3xl font-bold">
              {usage?.ramMbUsed.toFixed(0) ?? "—"}
              <span className="text-base text-muted-foreground ml-1">MB</span>
            </div>
            <p className="text-xs text-muted-foreground mt-1">Currently used</p>
          </CardBody>
        </Card>
        <Card>
          <CardHeader><CardTitle>Network</CardTitle></CardHeader>
          <CardBody>
            <div className="space-y-1">
              <div className="flex justify-between text-sm">
                <span className="text-muted-foreground">In</span>
                <span className="font-mono">{usage ? formatBytes(usage.networkBytesIn) : "—"}</span>
              </div>
              <div className="flex justify-between text-sm">
                <span className="text-muted-foreground">Out</span>
                <span className="font-mono">{usage ? formatBytes(usage.networkBytesOut) : "—"}</span>
              </div>
            </div>
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Events</CardTitle>
          <CardDescription className="mt-1">Live status transitions</CardDescription>
        </CardHeader>
        <CardBody>
          {allEvents.length === 0 ? (
            <p className="text-sm text-muted-foreground">No events yet.</p>
          ) : (
            <ol className="space-y-2 text-sm">
              {allEvents.slice().reverse().map((e) => (
                <li key={e.id} className="flex items-start gap-3 py-2 border-b border-border last:border-0">
                  <div className="text-xs font-mono text-muted-foreground whitespace-nowrap">
                    {formatRelativeTime(e.timestamp)}
                  </div>
                  <div className="flex-1">
                    <span className="font-medium">{e.newStatus}</span>
                    {e.oldStatus !== e.newStatus && (
                      <span className="text-muted-foreground"> (was {e.oldStatus})</span>
                    )}
                    {e.message && (
                      <span className="text-muted-foreground"> — {e.message}</span>
                    )}
                  </div>
                </li>
              ))}
            </ol>
          )}
        </CardBody>
      </Card>
    </div>
  );
}