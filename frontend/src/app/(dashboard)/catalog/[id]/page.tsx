"use client";

import { use, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { catalog, Flavor, OsImage, resources } from "@/lib/api";
import { Card, CardBody, CardHeader, CardTitle, CardDescription } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Input, Label } from "@/components/ui/Input";
import { Spinner, PageSpinner } from "@/components/ui/Spinner";
import { Cpu, MemoryStick, HardDrive, ArrowLeft, ArrowRight } from "lucide-react";
import Link from "next/link";
import { formatCurrency } from "@/lib/utils";

export default function ProvisionPage({ params }: { params: Promise<{ id: string }> }) {
  const { id: flavorId } = use(params);
  const router = useRouter();
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [imageId, setImageId] = useState<string>("");
  const [error, setError] = useState<string | null>(null);

  const { data: flavor, isLoading: flavorLoading } = useQuery({
    queryKey: ["flavor", flavorId],
    queryFn: () => catalog.flavor(flavorId),
  });

  const { data: images, isLoading: imagesLoading } = useQuery({
    queryKey: ["images"],
    queryFn: () => catalog.images(),
  });

  // Default image selection
  if (!imageId && images && images.length > 0) {
    setImageId(images[0].id);
  }

  const provision = useMutation({
    mutationFn: () =>
      resources.provision({ name: name || `vm-${Date.now()}`, flavorId, imageId }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["resources"] });
      router.push(`/resources/${r.id}`);
    },
    onError: (e) => setError(e instanceof Error ? e.message : "Provisioning failed"),
  });

  if (flavorLoading || imagesLoading) return <PageSpinner />;
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

function Spec({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div>
      <div className="flex items-center gap-1.5 text-muted-foreground">{icon}<span className="text-xs">{label}</span></div>
      <div className="font-mono font-medium mt-1">{value}</div>
    </div>
  );
}