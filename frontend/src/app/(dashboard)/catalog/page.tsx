"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { catalog } from "@/lib/api";
import { usePageTitle } from "@/lib/use-page-title";
import { Card, CardBody, CardHeader, CardTitle, CardDescription } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Cpu, HardDrive, MemoryStick, ArrowRight, AlertTriangle } from "lucide-react";
import { formatCurrency, getErrorMessage } from "@/lib/utils";
import { PageSpinner } from "@/components/ui/Spinner";

export default function DashboardCatalogPage() {
  usePageTitle("Catalog");
  const {
    data: flavors,
    isLoading,
    isError,
    error,
  } = useQuery({
    queryKey: ["catalog-flavors"],
    queryFn: () => catalog.flavors(),
  });

  if (isLoading) return <PageSpinner />;

  if (isError) {
    return (
      <div className="space-y-6 max-w-3xl">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Packages</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Choose a VM package to provision.
          </p>
        </div>
        <Card>
          <CardBody className="flex items-start gap-3">
            <AlertTriangle className="h-5 w-5 text-error shrink-0 mt-0.5" />
            <p className="text-sm text-error">
              {getErrorMessage(error, "Unable to load packages. Please try again later.")}
            </p>
          </CardBody>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Packages</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Choose a VM package to provision. Pay only for what you use.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {flavors?.map((flavor) => (
          <DashboardFlavorCard key={flavor.id} flavor={flavor} />
        ))}
      </div>
    </div>
  );
}

function DashboardFlavorCard({
  flavor,
}: {
  flavor: NonNullable<
    Awaited<ReturnType<typeof catalog.flavors>>
  >[number];
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between">
          <div>
            <CardTitle>{flavor.name}</CardTitle>
            <CardDescription className="mt-1">{flavor.category}</CardDescription>
          </div>
          <Badge variant={flavor.pricePerHour < 0.05 ? "info" : "default"}>
            {flavor.pricePerHour < 0.05 ? "Budget" : "Standard"}
          </Badge>
        </div>
      </CardHeader>
      <CardBody className="space-y-4">
        <div className="grid grid-cols-3 gap-3 text-sm">
          <Spec icon={<Cpu className="h-4 w-4" />} label="vCPU" value={String(flavor.vcpus)} />
          <Spec icon={<MemoryStick className="h-4 w-4" />} label="RAM" value={`${(flavor.ramMb / 1024).toFixed(0)} GB`} />
          <Spec icon={<HardDrive className="h-4 w-4" />} label="Disk" value={`${flavor.diskGb} GB`} />
        </div>
        <div className="pt-3 border-t border-border">
          <div className="flex items-baseline justify-between">
            <span className="text-sm text-muted-foreground">Per hour</span>
            <span className="font-mono text-lg font-semibold">{formatCurrency(flavor.pricePerHour)}</span>
          </div>
          <div className="flex items-baseline justify-between mt-1">
            <span className="text-sm text-muted-foreground">Per month</span>
            <span className="font-mono text-sm">{formatCurrency(flavor.pricePerMonth)}</span>
          </div>
        </div>
        <Button className="w-full" asChild>
          <Link href={`/catalog/${flavor.id}`}>
            Provision
            <ArrowRight className="h-4 w-4" />
          </Link>
        </Button>
      </CardBody>
    </Card>
  );
}

function Spec({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
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