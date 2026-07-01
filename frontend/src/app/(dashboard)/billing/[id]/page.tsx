"use client";

import { use } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";
import { invoices } from "@/lib/api";
import { usePageTitle } from "@/lib/use-page-title";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { InvoiceStatusBadge } from "@/components/ui/Badge";
import { PageSpinner } from "@/components/ui/Spinner";
import { formatCurrency, getErrorMessage } from "@/lib/utils";

export default function InvoiceDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  usePageTitle("Invoice");
  const qc = useQueryClient();

  const { data: inv, isLoading, isError, error } = useQuery({
    queryKey: ["invoice", id],
    queryFn: () => invoices.detail(id),
  });

  const pay = useMutation({
    mutationFn: () => invoices.pay(id),
    onSuccess: () => {
      toast.success("Invoice paid");
      qc.invalidateQueries({ queryKey: ["invoice", id] });
      qc.invalidateQueries({ queryKey: ["invoices"] });
    },
    onError: (e) => toast.error(getErrorMessage(e, "Payment failed")),
  });

  if (isLoading) return <PageSpinner />;

  if (isError) {
    return (
      <div className="space-y-6 max-w-3xl">
        <Link href="/billing" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
          <ArrowLeft className="h-3 w-3" />
          Back to invoices
        </Link>
        <Card>
          <CardBody>
            <p className="text-sm text-error">{getErrorMessage(error, "Unable to load invoice")}</p>
          </CardBody>
        </Card>
      </div>
    );
  }

  if (!inv) {
    return (
      <div className="space-y-6 max-w-3xl">
        <Link href="/billing" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
          <ArrowLeft className="h-3 w-3" />
          Back to invoices
        </Link>
        <Card>
          <CardBody>
            <p className="text-sm text-error">Invoice not found.</p>
          </CardBody>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-6 max-w-3xl">
      <Link href="/billing" className="text-sm text-muted-foreground hover:text-foreground inline-flex items-center gap-1">
        <ArrowLeft className="h-3 w-3" />
        Back to invoices
      </Link>

      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Invoice</h1>
          <p className="text-sm text-muted-foreground mt-1">
            {new Date(inv.periodStart).toLocaleDateString()} – {new Date(inv.periodEnd).toLocaleDateString()}
          </p>
        </div>
        <InvoiceStatusBadge status={inv.status} />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Line items</CardTitle>
        </CardHeader>
        <CardBody className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-sm">
              <thead>
                <tr className="border-b border-border text-muted-foreground">
                  <th className="px-6 py-3 text-left font-medium">Description</th>
                  <th className="px-6 py-3 text-right font-medium">Hours</th>
                  <th className="px-6 py-3 text-right font-medium">Rate</th>
                  <th className="px-6 py-3 text-right font-medium">Amount</th>
                </tr>
              </thead>
              <tbody>
                {inv.lines.map((line) => (
                  <tr key={line.id} className="border-b border-border last:border-0">
                    <td className="px-6 py-3">{line.description}</td>
                    <td className="px-6 py-3 text-right font-mono">{line.hours.toFixed(2)}</td>
                    <td className="px-6 py-3 text-right font-mono">{formatCurrency(line.rate)}</td>
                    <td className="px-6 py-3 text-right font-mono font-medium">{formatCurrency(line.amount)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-border">
                  <td colSpan={3} className="px-6 py-3 text-right font-semibold">Total</td>
                  <td className="px-6 py-3 text-right font-mono font-bold text-lg">
                    {formatCurrency(inv.total)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>
        </CardBody>
      </Card>

      {inv.status === "Pending" && (
        <div className="flex justify-end">
          <Button size="lg" onClick={() => pay.mutate()} disabled={pay.isPending}>
            {pay.isPending ? "Processing…" : `Pay ${formatCurrency(inv.total)}`}
          </Button>
        </div>
      )}
      {inv.status === "Paid" && inv.paidAt && (
        <p className="text-sm text-muted-foreground text-right">
          Paid on {new Date(inv.paidAt).toLocaleDateString()}
        </p>
      )}
    </div>
  );
}
