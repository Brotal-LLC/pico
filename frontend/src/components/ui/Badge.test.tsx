import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Badge, InvoiceStatusBadge, StatusBadge } from "@/components/ui/Badge";

describe("Badge", () => {
  it("renders children and applies the default variant", () => {
    render(<Badge>Hello</Badge>);
    const el = screen.getByText("Hello");
    expect(el).toBeInTheDocument();
    expect(el).toHaveClass("inline-flex");
  });

  it("supports an explicit variant", () => {
    render(<Badge variant="success">OK</Badge>);
    expect(screen.getByText("OK")).toHaveClass("text-success");
  });

  it("merges custom className", () => {
    render(<Badge className="mt-4">Merged</Badge>);
    expect(screen.getByText("Merged")).toHaveClass("mt-4");
  });
});

describe("StatusBadge", () => {
  it.each([
    ["Running", "text-success"],
    ["Provisioning", "text-accent"],
    ["Stopped", "text-warning"],
    ["Failed", "text-error"],
    ["Unknown", "text-foreground"],
  ] as const)("maps %s to %s", (status, expectedClass) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByText(status)).toHaveClass(expectedClass);
  });
});

describe("InvoiceStatusBadge", () => {
  it("maps Paid to success", () => {
    render(<InvoiceStatusBadge status="Paid" />);
    expect(screen.getByText("Paid")).toHaveClass("text-success");
  });

  it("maps Pending to warning", () => {
    render(<InvoiceStatusBadge status="Pending" />);
    expect(screen.getByText("Pending")).toHaveClass("text-warning");
  });

  it("defaults to neutral for unknown statuses", () => {
    render(<InvoiceStatusBadge status="Voided" />);
    expect(screen.getByText("Voided")).toHaveClass("text-foreground");
  });
});
