"use client";

import { use, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { catalog, resources, ProvisioningPlan } from "@/lib/api";
import { usePageTitle } from "@/lib/use-page-title";
import { Card, CardBody, CardHeader, CardTitle, CardDescription } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Input, Label } from "@/components/ui/Input";
import { Spinner, PageSpinner } from "@/components/ui/Spinner";
import { Cpu, MemoryStick, HardDrive, ArrowLeft, ArrowRight, AlertTriangle, CheckCircle2, Eye } from "lucide-react";
import Link from "next/link";
import { formatCurrency, getErrorMessage } from "@/lib/utils";

export default function ProvisionPage({ params }: { params: Promise<{ id: string }> }) {
  const { id: flavorId } = use(params);
  usePageTitle("Provision");
  const router = useRouter();
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [imageId, setImageId] = useState<string>("");
  const [error, setError] = useState<string | null>(null);

  const {
    data: flavor,
    isLoading: flavorLoading,
    isError: flavorIsError,
    error: flavorError,
  } = useQuery({
    queryKey: ["flavor", flavorId],
    queryFn: () => catalog.flavor(flavorId),
  });

  const {
    data: images,
    isLoading: imagesLoading,
    isError: imagesIsError,
    error: imagesError,
  } = useQuery({
    queryKey: ["images"],
    queryFn: () => catalog.images(),
  });

  useEffect(() => {
    if (!imageId && images && images.length > 0) {
      setImageId(images[0].id);
    }
  }, [imageId, images]);

  // Terraform-style preview: only fires after the user has picked an image
  // (otherwise the first paint shows a spinner instead of the empty state).
  const previewEnabled = Boolean(imageId);
  const {
    data: plan,
    isFetching: planFetching,
    isError: planIsError,
    error: planError,
  } = useQuery<ProvisioningPlan>({
    queryKey: ["provision-plan", flavorId, imageId],
    queryFn: () => resources.preview({ flavorId, imageId }),
    enabled: previewEnabled,
    staleTime: 5_000,
  });

  const provision = useMutation({
    mutationFn: () =>
      resources.provision({ name: name || `vm-${Date.now()}`, flavorId, imageId }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["resources"] });
      router.push(`/resources/${r.id}`);
    },
    onError: (e) => setError(getErrorMessage(e, "Provisioning failed")),
  });

  if (flavorLoading || imagesLoading) return <PageSpinner />;

  if (flavorIsError || imagesIsError) {
    return (
      <div className="space-y-6 max-w-3xl">
        <Link href="/catalog" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
          <ArrowLeft className="h-3 w-3" />
          Back to catalog
        </Link>
        <Card>
          <CardBody>
            <p className="text-sm text-error">
              {getErrorMessage(flavorError ?? imagesError, "Unable to load provisioning details")}
            </p>
          </CardBody>
        </Card>
      </div>
    );
  }

  if (!flavor) return <div>Package not found.</div>;

  return (
    <div className="space-y-6 max-w-3xl">
      <Link href="/catalog" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
        <ArrowLeft className="h-3 w-3" />
        Back to catalog
      </Link>

      <div>
        <h1 className="text-3xl font-bold tracking-tight">Provision</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Configure your new resource
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <Card>
          <CardHeader>
            <CardTitle>Selected package</CardTitle>
          </CardHeader>
          <CardBody className="space-y-3">
            <h3 className="text-xl font-semibold">{flavor.name}</h3>
            <p className="text-sm text-muted-foreground">{flavor.category}</p>
            <div className="grid grid-cols-3 gap-3 text-sm pt-2">
              <Spec icon={<Cpu />} label="vCPU" value={String(flavor.vcpus)} />
              <Spec icon={<MemoryStick />} label="RAM" value={`${(flavor.ramMb / 1024).toFixed(0)} GB`} />
              <Spec icon={<HardDrive />} label="Disk" value={`${flavor.diskGb} GB`} />
            </div>
            <div className="pt-3 border-t border-border text-sm">
              <div className="flex justify-between">
                <span className="text-muted-foreground">Per hour</span>
                <span className="font-mono font-semibold">{formatCurrency(flavor.pricePerHour)}</span>
              </div>
              <div className="flex justify-between mt-1">
                <span className="text-muted-foreground">Per month</span>
                <span className="font-mono">{formatCurrency(flavor.pricePerMonth)}</span>
              </div>
            </div>
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Configuration</CardTitle>
            <CardDescription className="mt-1">Pick an OS image and name your VM</CardDescription>
          </CardHeader>
          <CardBody className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="name">Resource name</Label>
              <Input
                id="name"
                placeholder={`my-${flavor.name}`}
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="image">OS image</Label>
              <select
                id="image"
                value={imageId}
                onChange={(e) => setImageId(e.target.value)}
                className="flex h-10 w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
              >
                {images?.map((img) => (
                  <option key={img.id} value={img.id}>
                    {img.os} {img.version} ({img.sizeGb} GB)
                  </option>
                ))}
              </select>
            </div>

            {/* Plan preview — Terraform-style "what will happen if I commit?". */}
            {previewEnabled && (
              <PlanCard
                plan={plan}
                isFetching={planFetching}
                isError={planIsError}
                error={planError}
              />
            )}

            {error && <p className="text-sm text-error">{error}</p>}
            <Button
              className="w-full"
              onClick={() => provision.mutate()}
              disabled={provision.isPending || !imageId}
            >
              {provision.isPending ? (
                <>
                  <Spinner className="h-4 w-4" />
                  Provisioning…
                </>
              ) : (
                <>
                  Provision
                  <ArrowRight className="h-4 w-4" />
                </>
              )}
            </Button>
          </CardBody>
        </Card>
      </div>
    </div>
  );
}

/**
 * Terraform-style preview card. Pure — never creates a resource.
 * Re-renders whenever the user picks a different (flavor, image) pair.
 */
function PlanCard({
  plan,
  isFetching,
  isError,
  error,
}: {
  plan: ProvisioningPlan | undefined;
  isFetching: boolean;
  isError: boolean;
  error: unknown;
}) {
  if (isFetching && !plan) {
    return (
      <div className="rounded-md border border-dashed border-border bg-muted/40 p-3 text-sm text-muted-foreground flex items-center gap-2">
        <Eye className="h-4 w-4" />
        Computing provision plan…
      </div>
    );
  }
  if (isError || !plan) {
    return (
      <div className="rounded-md border border-error/30 bg-error/10 p-3 text-sm text-error flex items-center gap-2">
        <AlertTriangle className="h-4 w-4" />
        {getErrorMessage(error, "Unable to compute provision plan")}
      </div>
    );
  }

  const fitsDisk = plan.imageFitsInFlavorDisk;

  return (
    <div className="rounded-md border border-border bg-muted/30 p-3 text-sm space-y-2">
      <div className="flex items-center gap-2 text-muted-foreground text-xs uppercase tracking-wide">
        <Eye className="h-3 w-3" />
        Provisioning plan
        {isFetching && <Spinner className="h-3 w-3 ml-auto" />}
      </div>
      <div className="grid grid-cols-2 gap-x-3 gap-y-1 text-xs">
        <span className="text-muted-foreground">Estimated monthly</span>
        <span className="font-mono font-medium text-right">
          {formatCurrency(plan.monthlyCostEstimate)}
        </span>
        <span className="text-muted-foreground">Estimated hourly</span>
        <span className="font-mono text-right">
          {formatCurrency(plan.hourlyCostEstimate)}
        </span>
        <span className="text-muted-foreground">OS image</span>
        <span className="font-mono text-right">
          {plan.imageOs} {plan.imageVersion} ({plan.imageSizeGb} GB)
        </span>
        <span className="text-muted-foreground">Image fits in disk</span>
        <span className="font-mono text-right inline-flex items-center justify-end gap-1">
          {fitsDisk ? (
            <>
              <CheckCircle2 className="h-3 w-3 text-success" />
              Yes
            </>
          ) : (
            <>
              <AlertTriangle className="h-3 w-3 text-warning" />
              No — backend will resize
            </>
          )}
        </span>
      </div>
      {plan.warnings.length > 0 && (
        <ul className="space-y-1 pt-1 border-t border-border">
          {plan.warnings.map((w, i) => (
            <li
              key={i}
              className="text-xs text-warning-foreground bg-warning/20 rounded px-2 py-1 flex items-start gap-2"
            >
              <AlertTriangle className="h-3 w-3 mt-0.5 shrink-0" />
              <span>{w}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function Spec({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div>
      <div className="flex items-center gap-1.5 text-muted-foreground">{icon}<span className="text-xs">{label}</span></div>
      <div className="font-mono font-medium mt-1">{value}</div>
    </div>
  );
}
