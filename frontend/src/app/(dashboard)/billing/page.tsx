"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { toast } from "sonner";
import { invoices, InvoiceListItem } from "@/lib/api";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { InvoiceStatusBadge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageSpinner } from "@/components/ui/Spinner";
import { Receipt, ExternalLink } from "lucide-react";
import { formatCurrency, formatRelativeTime, getErrorMessage } from "@/lib/utils";

export default function BillingPage() {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ["invoices"],
    queryFn: () => invoices.list(),
  });

  if (isLoading) return <PageSpinner />;

  if (isError) {
    return (
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Billing</h1>
          <p className="text-sm text-muted-foreground mt-1">Your monthly invoices</p>
        </div>
        <Card>
          <CardBody>
            <p className="text-sm text-error">{getErrorMessage(error, "Unable to load invoices")}</p>
          </CardBody>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Billing</h1>
        <p className="text-sm text-muted-foreground mt-1">
          Your monthly invoices
        </p>
      </div>

      {data?.length === 0 ? (
        <EmptyState
          icon={<Receipt className="h-10 w-10" />}
          title="No invoices yet"
          description="Invoices are generated monthly based on your active resources."
        />
      ) : (
        <Card>
          <CardHeader>
            <CardTitle>Invoices</CardTitle>
          </CardHeader>
          <CardBody className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[640px] text-sm">
                <thead>
                  <tr className="border-b border-border text-muted-foreground">
                    <th className="px-6 py-3 text-left font-medium">Period</th>
                    <th className="px-6 py-3 text-left font-medium">Status</th>
                    <th className="px-6 py-3 text-right font-medium">Total</th>
                    <th className="px-6 py-3 text-right font-medium">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {data?.map((inv) => (
                    <InvoiceRow key={inv.id} invoice={inv} />
                  ))}
                </tbody>
              </table>
            </div>
          </CardBody>
        </Card>
      )}
    </div>
  );
}

function InvoiceRow({ invoice: inv }: { invoice: InvoiceListItem }) {
  const qc = useQueryClient();
  const pay = useMutation({
    mutationFn: () => invoices.pay(inv.id),
    onSuccess: () => {
      toast.success("Invoice paid");
      qc.invalidateQueries({ queryKey: ["invoices"] });
    },
    onError: (e) => toast.error(getErrorMessage(e, "Payment failed")),
  });

  const start = new Date(inv.periodStart).toLocaleDateString("en-US", { month: "short", day: "numeric" });
  const end = new Date(inv.periodEnd).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });

  return (
    <tr className="border-b border-border last:border-0 hover:bg-muted/30">
      <td className="px-6 py-3">
        <div className="font-medium">{start} – {end}</div>
        <div className="text-xs text-muted-foreground mt-0.5">
          Created {formatRelativeTime(inv.createdAt)} · {inv.lineCount} line items
        </div>
      </td>
      <td className="px-6 py-3"><InvoiceStatusBadge status={inv.status} /></td>
      <td className="px-6 py-3 text-right font-mono font-semibold">
        {formatCurrency(inv.total)}
      </td>
      <td className="px-6 py-3 text-right space-x-2 whitespace-nowrap">
        <Button variant="ghost" size="sm" asChild>
          <Link href={`/billing/${inv.id}`}>
            <ExternalLink className="h-3 w-3" />
            View
          </Link>
        </Button>
        {inv.status === "Pending" && (
          <Button
            size="sm"
            onClick={() => pay.mutate()}
            disabled={pay.isPending}
          >
            Pay now
          </Button>
        )}
      </td>
    </tr>
  );
}
