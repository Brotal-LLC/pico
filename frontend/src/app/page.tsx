"use client";

import { Button } from "@/components/ui/Button";
import Link from "next/link";
import { ArrowRight, Cloud } from "lucide-react";

export default function HomePage() {
  return (
    <main className="min-h-screen bg-background">
      <header className="border-b border-border">
        <div className="mx-auto max-w-6xl px-6 py-4 flex items-center justify-between">
          <h1 className="text-2xl font-bold tracking-tight">Pico</h1>
          <div className="flex gap-2">
            <Link href="/login">
              <Button variant="ghost">Sign in</Button>
            </Link>
            <Link href="/signup">
              <Button variant="primary">Get started</Button>
            </Link>
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
          <Link href="/signup">
            <Button size="lg">
              Create account
              <ArrowRight className="h-4 w-4" />
            </Button>
          </Link>
          <Link href="/catalog">
            <Button variant="outline" size="lg">Browse packages</Button>
          </Link>
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
            <h3 className="font-semibold">Usage metering</h3>
            <p className="text-sm text-muted-foreground mt-1">
              Real-time CPU, RAM, and network stats. No surprises on your bill.
            </p>
          </div>
          <div className="border border-border rounded-lg p-6">
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