import Link from "next/link";
import type { Metadata } from "next";
import { cookies } from "next/headers";
import { catalog } from "@/lib/api";
import { Card, CardBody, CardHeader, CardTitle, CardDescription } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";

export const metadata: Metadata = {
  title: "Catalog",
  description: "Browse Pico's VM flavors and OS images, then provision in seconds.",
};
import { Cpu, HardDrive, MemoryStick, ArrowRight, ArrowLeft } from "lucide-react";
import { formatCurrency } from "@/lib/utils";

export const dynamic = "force-dynamic";

export default async function PublicCatalogPage() {
  let flavors: Awaited<ReturnType<typeof catalog.flavors>> | null = null;
  let error: string | null = null;

  try {
    flavors = await catalog.flavors();
  } catch (e) {
    error = e instanceof Error ? e.message : "Unable to load packages. Please try again later.";
  }

  // Detect an active session by looking for the auth cookie. This avoids a
  // server-to-server `auth.me()` round-trip on every render and keeps the
  // page snapshot-friendly for anonymous visitors.
  const cookieStore = await cookies();
  const sessionToken = cookieStore.get("Pico.Auth")?.value;
  const isAuthenticated = Boolean(sessionToken);

  return (
    <main className="min-h-screen bg-background">
      <header className="border-b border-border">
        <div className="mx-auto max-w-6xl px-6 py-4 flex items-center justify-between">
          <Link href="/" className="text-2xl font-bold tracking-tight">
            Pico
          </Link>
          <div className="flex gap-2">
            {isAuthenticated ? (
              <>
                <Button variant="ghost" asChild>
                  <Link href="/dashboard">Dashboard</Link>
                </Button>
                <Button variant="primary" asChild>
                  <Link href="/dashboard">Provision</Link>
                </Button>
              </>
            ) : (
              <>
                <Button variant="ghost" asChild>
                  <Link href="/login">Sign in</Link>
                </Button>
                <Button variant="primary" asChild>
                  <Link href="/signup">Get started</Link>
                </Button>
              </>
            )}
          </div>
        </div>
      </header>

      <section className="mx-auto max-w-6xl px-6 py-12">
        <Link
          href={isAuthenticated ? "/dashboard" : "/"}
          className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1 mb-6"
        >
          <ArrowLeft className="h-3 w-3" />
          {isAuthenticated ? "Back to dashboard" : "Back to home"}
        </Link>

        <h1 className="text-3xl font-bold tracking-tight">Packages</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Choose a VM package to provision. Pay only for what you use.
        </p>

        {error ? (
          <Card className="mt-6">
            <CardBody>
              <p className="text-sm text-error">{error}</p>
            </CardBody>
          </Card>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 mt-6">
            {flavors?.map((flavor) => (
              <FlavorCard key={flavor.id} flavor={flavor} isAuthenticated={isAuthenticated} />
            ))}
          </div>
        )}
      </section>
    </main>
  );
}

function FlavorCard({
  flavor,
  isAuthenticated,
}: {
  flavor: NonNullable<Awaited<ReturnType<typeof catalog.flavors>>>[number];
  isAuthenticated: boolean;
}) {
  const ctaHref = isAuthenticated ? `/catalog/${flavor.id}` : "/signup";
  const ctaLabel = isAuthenticated ? "Provision" : "Get started";

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
          <Link href={ctaHref}>
            {ctaLabel}
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
