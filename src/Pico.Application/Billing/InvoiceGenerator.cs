using Pico.Domain.Entities;

namespace Pico.Application.Billing;

/// <summary>
/// Generates invoices from resource usage lines for a billing period.
/// Pure logic: no DB access, no IO. Returns null when there are no billable items.
/// </summary>
public class InvoiceGenerator
{
    /// <summary>
    /// Build a single invoice from one or more usage lines for a single user.
    /// Returns null if the user has no billable activity in the period (no line items).
    /// </summary>
    public Invoice? Generate(
        Guid userId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        IReadOnlyList<ResourceUsageLine> usageLines)
    {
        ArgumentNullException.ThrowIfNull(usageLines);
        if (usageLines.Count == 0) return null;

        var lines = new List<InvoiceLine>();
        foreach (var line in usageLines)
        {
            if (line.HoursInPeriod <= 0) continue;

            var amount = decimal.Round(
                line.Flavor.PricePerHour * line.HoursInPeriod,
                2,
                MidpointRounding.AwayFromZero);

            // Build invoice line directly so we don't need a persisted InvoiceId yet
            var invoiceLine = new InvoiceLine(
                invoiceId: Guid.Empty, // assigned by EF when persisted
                resourceId: line.Resource.Id,
                flavorId: line.Flavor.Id,
                hours: line.HoursInPeriod,
                rate: line.Flavor.PricePerHour,
                amount: amount,
                description: line.Description);
            lines.Add(invoiceLine);
        }

        if (lines.Count == 0) return null;

        return Invoice.Create(userId, periodStart, periodEnd, lines);
    }
}
