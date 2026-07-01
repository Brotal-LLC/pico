import { redirect } from "next/navigation";
import Link from "next/link";
import { Button } from "@/components/ui/Button";
import { ArrowRight, Cloud, Activity, Receipt } from "lucide-react";
import { auth } from "@/lib/api";

export default async function HomePage() {
  // Server-side check: if user has a valid session, send to dashboard
  try {
    await auth.me();
    redirect("/dashboard");
  } catch {
    // Not logged in, show landing
  }

  return (
    <main className="min-h-screen bg-background">
      <header className="border-b border-border">
        <div className="mx-auto max-w-6xl px-6 py-4 flex items-center justify-between">
          <h1 className="text-2xl font-bold tracking-tight">Pico</h1>
          <div className="flex gap-2">
            <Button variant="ghost" asChild>
              <Link href="/login">Sign in</Link>
            </Button>
            <Button variant="primary" asChild>
              <Link href="/signup">Get started</Link>
            </Button>
          </div>
        </div>
      </header>

      <section className="mx-auto max-w-6xl px-6 py-24">
        <h2 className="text-5xl font-bold tracking-tight">Self-service cloud infrastructure</h2>
        <p className="mt-4 text-lg text-muted-foreground max-w-2xl">
          Provision VMs, monitor usage, and manage billing — all without touching a support ticket.
          Pay only for what you use.
        </p>
        <div className="mt-8 flex gap-3">
          <Button size="lg" asChild>
            <Link href="/signup">
              Create account
              <ArrowRight className="h-4 w-4" />
            </Link>
          </Button>
          <Button variant="outline" size="lg" asChild>
            <Link href="/catalog">Browse packages</Link>
          </Button>
        </div>
      </section>

      <section className="mx-auto max-w-6xl px-6 pb-24">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          <div className="border border-border rounded-lg p-6">
            <Cloud className="h-6 w-6 mb-3 text-accent" />
            <h3 className="font-semibold">Provision in minutes</h3>
            <p className="text-sm text-muted-foreground mt-1">
              Choose a package, pick an OS image, and your VM is live in seconds.
            </p>
          </div>
          <div className="border border-border rounded-lg p-6">
            <Activity className="h-6 w-6 mb-3 text-accent" />
            <h3 className="font-semibold">Usage metering</h3>
            <p className="text-sm text-muted-foreground mt-1">
              Real-time CPU, RAM, and network stats. No surprises on your bill.
            </p>
          </div>
          <div className="border border-border rounded-lg p-6">
            <Receipt className="h-6 w-6 mb-3 text-accent" />
            <h3 className="font-semibold">Transparent billing</h3>
            <p className="text-sm text-muted-foreground mt-1">
              Hourly and monthly pricing. Monthly invoices with detailed line items.
            </p>
          </div>
        </div>
      </section>
    </main>
  );
}
