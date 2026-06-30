"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { resources, ResourceSummary } from "@/lib/api";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { StatusBadge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageSpinner } from "@/components/ui/Spinner";
import { Plus, Server } from "lucide-react";
import { formatRelativeTime } from "@/lib/utils";

export default function DashboardPage() {
  const { data: resourcesList, isLoading } = useQuery({
    queryKey: ["resources"],
    queryFn: () => resources.list(),
  });

  if (isLoading) return <PageSpinner />;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Resources</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Provisioned cloud instances
          </p>
        </div>
        <Link href="/catalog">
          <Button>
            <Plus className="h-4 w-4" />
            Provision
          </Button>
        </Link>
      </div>

      {resourcesList?.length === 0 ? (
        <EmptyState
          icon={<Server className="h-10 w-10" />}
          title="No resources yet"
          description="Provision your first virtual machine to get started."
          action={
            <Link href="/catalog">
              <Button>
                <Plus className="h-4 w-4" />
                Browse catalog
              </Button>
            </Link>
          }
        />
      ) : (
        <Card>
          <CardHeader>
            <CardTitle>Your resources</CardTitle>
          </CardHeader>
          <CardBody className="p-0">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-muted-foreground">
                  <th className="px-6 py-3 text-left font-medium">Name</th>
                  <th className="px-6 py-3 text-left font-medium">Status</th>
                  <th className="px-6 py-3 text-left font-medium">IP</th>
                  <th className="px-6 py-3 text-left font-medium">Created</th>
                  <th className="px-6 py-3 text-right font-medium">Actions</th>
                </tr>
              </thead>
              <tbody>
                {resourcesList?.map((r) => (
                  <ResourceRow key={r.id} resource={r} />
                ))}
              </tbody>
            </table>
          </CardBody>
        </Card>
      )}
    </div>
  );
}

function ResourceRow({ resource: r }: { resource: ResourceSummary }) {
  return (
    <tr className="border-b border-border last:border-0 hover:bg-muted/30">
      <td className="px-6 py-3">
        <Link href={`/resources/${r.id}`} className="font-medium hover:underline">
          {r.name}
        </Link>
      </td>
      <td className="px-6 py-3"><StatusBadge status={r.status} /></td>
      <td className="px-6 py-3 font-mono text-xs">{r.ipAddress ?? "—"}</td>
      <td className="px-6 py-3 text-muted-foreground">{formatRelativeTime(r.createdAt)}</td>
      <td className="px-6 py-3 text-right">
        <Link href={`/resources/${r.id}`}>
          <Button variant="ghost" size="sm">View</Button>
        </Link>
      </td>
    </tr>
  );
}