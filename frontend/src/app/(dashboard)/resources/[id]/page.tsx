"use client";

import { use, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { toast } from "sonner";
import { resources, ResourceDetail, ResourceEvent, catalog } from "@/lib/api";
import { usePageTitle } from "@/lib/use-page-title";
import { Card, CardBody, CardHeader, CardTitle, CardDescription } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { StatusBadge } from "@/components/ui/Badge";
import { PageSpinner } from "@/components/ui/Spinner";
import {
  ArrowLeft,
  Play,
  Square,
  Trash2,
  RotateCcw,
  Cpu,
  MemoryStick,
  HardDrive,
} from "lucide-react";
import { formatBytes, formatRelativeTime, getErrorMessage } from "@/lib/utils";
import { VmShellPanel } from "@/components/VmShellPanel";

function dedupeEvents(items: ResourceEvent[]) {
  const seen = new Set<string>();
  return items.filter((event) => {
    if (seen.has(event.id)) return false;
    seen.add(event.id);
    return true;
  });
}

/**
 * Resources reach a handful of "operable" states where lifecycle actions
 * make sense. Outside those (Created/Provisioning transitions are
 * system-driven; Terminated/Failed are terminal), we either hide the
 * controls (during Provisioning) or replace them with a Recreate CTA
 * (for Terminated/Failed historical VMs).
 */
function getLifecycleMode(detail: ResourceDetail | undefined): "operable" | "provisioning" | "historical" | "failed" {
  if (!detail) return "operable";
  if (detail.status === "Provisioning" || detail.status === "Created") return "provisioning";
  if (detail.status === "Terminated") return "historical";
  if (detail.status === "Failed") return "failed";
  return "operable"; // Running | Stopped
}

export default function ResourceDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  usePageTitle("Resource");
  const router = useRouter();
  const qc = useQueryClient();

  const {
    data: detail,
    isLoading,
    isError,
    error,
  } = useQuery({
    queryKey: ["resource", id],
    queryFn: () => resources.detail(id),
    refetchInterval: (q) => {
      const data = q.state.data as ResourceDetail | undefined;
      // Auto-refetch while non-terminal state for live status updates
      if (data && !["Terminated", "Failed"].includes(data.status)) return 5000;
      return false;
    },
  });

  const {
    data: usage,
    isError: usageIsError,
    error: usageError,
  } = useQuery({
    queryKey: ["resource-usage", id],
    queryFn: () => resources.usage(id),
    refetchInterval: 5000,
  });

  // Flavor + image for the "Recreate with same config" summary card.
  // Fetched lazily so operable VMs don't pay for this round-trip.
  const showRecreateContext = getLifecycleMode(detail) === "historical" || getLifecycleMode(detail) === "failed";
  const { data: flavor } = useQuery({
    queryKey: ["flavor", detail?.flavorId],
    queryFn: () => catalog.flavor(detail!.flavorId),
    enabled: showRecreateContext && Boolean(detail?.flavorId),
  });
  const { data: image } = useQuery({
    queryKey: ["image", detail?.imageId],
    queryFn: async () => {
      const images = await catalog.images();
      return images.find((i) => i.id === detail!.imageId) ?? null;
    },
    enabled: showRecreateContext && Boolean(detail?.imageId),
  });

  const [events, setEvents] = useState<ResourceEvent[]>([]);

  // SSE: listen for live status updates
  useEffect(() => {
    if (!id) return;
    const es = new EventSource(resources.eventsUrl(id), { withCredentials: true });
    es.onmessage = (e) => {
      try {
        const evt = JSON.parse(e.data) as ResourceEvent;
        setEvents((prev) => {
          if (prev.some((item) => item.id === evt.id)) return prev;
          return [...prev, evt].slice(-50);
        });
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
    onError: (e) => toast.error(getErrorMessage(e, "Start failed")),
  });
  const stop = useMutation({
    mutationFn: () => resources.stop(id),
    onSuccess: () => {
      toast.success("Resource stopping");
      qc.invalidateQueries({ queryKey: ["resource", id] });
    },
    onError: (e) => toast.error(getErrorMessage(e, "Stop failed")),
  });
  const terminate = useMutation({
    mutationFn: () => resources.terminate(id),
    onSuccess: () => {
      toast.success("Resource terminated");
      qc.invalidateQueries({ queryKey: ["resources"] });
      qc.invalidateQueries({ queryKey: ["resource", id] });
      router.push("/dashboard");
    },
    onError: (e) => toast.error(getErrorMessage(e, "Terminate failed")),
  });
  const recreate = useMutation({
    mutationFn: () => resources.recreate(id),
    onSuccess: (newResource) => {
      toast.success(`Created ${newResource.name}`);
      qc.invalidateQueries({ queryKey: ["resources"] });
      router.push(`/resources/${newResource.id}`);
    },
    onError: (e) => toast.error(getErrorMessage(e, "Recreate failed")),
  });

  if (isLoading) return <PageSpinner />;

  if (isError) {
    return (
      <div className="space-y-6">
        <Link href="/dashboard" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
          <ArrowLeft className="h-3 w-3" />
          Back to dashboard
        </Link>
        <Card>
          <CardBody>
            <p className="text-sm text-error">{getErrorMessage(error, "Unable to load resource")}</p>
          </CardBody>
        </Card>
      </div>
    );
  }

  if (!detail) {
    return (
      <div className="space-y-6">
        <Link href="/dashboard" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
          <ArrowLeft className="h-3 w-3" />
          Back to dashboard
        </Link>
        <Card>
          <CardBody>
            <p className="text-sm text-error">Resource not found.</p>
          </CardBody>
        </Card>
      </div>
    );
  }

  const allEvents = dedupeEvents([...(detail.events ?? []), ...events]);
  const mode = getLifecycleMode(detail);

  const confirmTerminate = () => {
    if (window.confirm(`Terminate ${detail.name}? This action cannot be undone.`)) {
      terminate.mutate();
    }
  };
  const confirmRecreate = () => {
    if (window.confirm(`Recreate ${detail.name} with the same configuration?`)) {
      recreate.mutate();
    }
  };

  return (
    <div className="space-y-6">
      <Link href="/dashboard" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
        <ArrowLeft className="h-3 w-3" />
        Back to dashboard
      </Link>

      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">{detail.name}</h1>
          <div className="flex items-center gap-2 mt-2">
            <StatusBadge status={detail.status} />
            {detail.ipAddress && (
              <span className="text-sm font-mono text-muted-foreground">{detail.ipAddress}</span>
            )}
          </div>
        </div>

        {/*
         * Action area — three modes:
         *
         *  1. operable    — Running | Stopped. Show all three buttons; the
         *                   server enforces state-machine validity, so users
         *                   can try Start on a Running VM and get a clear
         *                   "Invalid transition" toast.
         *  2. provisioning — hide controls; the system is mid-flight.
         *  3. historical   — Terminated | Failed. Replace controls with a
         *                   "Recreate with same config" CTA so the user can
         *                   bring the same flavor/image back to life.
         */}
        {mode === "operable" && (
          <div className="flex flex-wrap gap-2">
            {/*
             * Lifecycle buttons with proper state management:
             *
             *  Start   — disabled when already Running; shows
             *            "Starting…" while the mutation is in flight.
             *            After success, the 5s refetch picks up the
             *            Running status and the button stays disabled.
             *
             *  Stop    — disabled when already Stopped; shows
             *            "Stopping…" while the mutation is in flight.
             *            After success, the refetch picks up Stopped
             *            and the button stays disabled.
             *
             *  Terminate — always enabled in operable mode (the
             *              confirm dialog is the guard, not the
             *              button). Shows "Terminating…" while pending.
             *
             *  A mutation on one button disables all three to prevent
             *  conflicting actions (e.g. pressing Stop while Start is
             *  mid-flight).
             */}
            <Button
              variant="outline"
              onClick={() => start.mutate()}
              disabled={start.isPending || stop.isPending || terminate.isPending || detail.status === "Running"}
            >
              <Play className="h-4 w-4" />
              {start.isPending ? "Starting…" : "Start"}
            </Button>
            <Button
              variant="outline"
              onClick={() => stop.mutate()}
              disabled={start.isPending || stop.isPending || terminate.isPending || detail.status === "Stopped"}
            >
              <Square className="h-4 w-4" />
              {stop.isPending ? "Stopping…" : "Stop"}
            </Button>
            <Button
              variant="danger"
              onClick={confirmTerminate}
              disabled={start.isPending || stop.isPending || terminate.isPending}
            >
              <Trash2 className="h-4 w-4" />
              {terminate.isPending ? "Terminating…" : "Terminate"}
            </Button>
          </div>
        )}

        {mode === "provisioning" && (
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" disabled>
              <Play className="h-4 w-4" />
              Provisioning…
            </Button>
            <Button
              variant="danger"
              onClick={confirmTerminate}
              disabled={terminate.isPending}
            >
              <Trash2 className="h-4 w-4" />
              Terminate
            </Button>
          </div>
        )}

        {(mode === "historical" || mode === "failed") && (
          <Button
            variant="primary"
            onClick={confirmRecreate}
            disabled={recreate.isPending}
          >
            <RotateCcw className="h-4 w-4" />
            {recreate.isPending ? "Recreating…" : "Recreate with same config"}
          </Button>
        )}
      </div>

      {mode === "historical" && flavor && image && (
        <Card>
          <CardHeader>
            <CardTitle>Original configuration</CardTitle>
            <CardDescription className="mt-1">
              Recreating will provision a new VM with these specs.
            </CardDescription>
          </CardHeader>
          <CardBody>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
              <ConfigItem
                icon={<Cpu className="h-4 w-4" />}
                label="Package"
                value={flavor.name}
              />
              <ConfigItem
                icon={<MemoryStick className="h-4 w-4" />}
                label="Image"
                value={`${image.os} ${image.version}`}
              />
              <ConfigItem
                icon={<HardDrive className="h-4 w-4" />}
                label="Resources"
                value={`${flavor.vcpus} vCPU · ${(flavor.ramMb / 1024).toFixed(0)} GB RAM`}
              />
              <ConfigItem
                icon={<HardDrive className="h-4 w-4" />}
                label="Created"
                value={formatRelativeTime(detail.createdAt)}
              />
            </div>
          </CardBody>
        </Card>
      )}

      {usageIsError && (
        <Card>
          <CardBody>
            <p className="text-sm text-error">{getErrorMessage(usageError, "Unable to load usage")}</p>
          </CardBody>
        </Card>
      )}

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

      {/* Interactive shell — only shown when VM is running */}
      <VmShellPanel resourceId={id} isRunning={detail.status === "Running"} />

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

function ConfigItem({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
}) {
  return (
    <div>
      <div className="flex items-center gap-1.5 text-muted-foreground">
        {icon}
        <span className="text-xs">{label}</span>
      </div>
      <div className="font-mono font-medium mt-1">{value}</div>
    </div>
  );
}