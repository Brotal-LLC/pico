using Pico.Domain.Enums;

namespace Pico.Domain.Entities;

/// <summary>
/// Invoice aggregate: a customer's monthly bill with line items per resource.
/// </summary>
public class Invoice
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset PeriodStart { get; private set; }
    public DateTimeOffset PeriodEnd { get; private set; }
    public decimal Total { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    private readonly List<InvoiceLine> _lines = new();
    public IReadOnlyList<InvoiceLine> Lines => _lines.AsReadOnly();

    private Invoice() { }

    public static Invoice Create(
        Guid userId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        IEnumerable<InvoiceLine> lines)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));
        if (periodEnd <= periodStart)
            throw new ArgumentException("Period end must be after period start.");
        ArgumentNullException.ThrowIfNull(lines);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Status = InvoiceStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        decimal total = 0;
        foreach (var line in lines)
        {
            invoice._lines.Add(line);
            total += line.Amount;
        }
        invoice.Total = total;

        return invoice;
    }

    public void MarkPaid(DateTimeOffset paidAt)
    {
        if (Status == InvoiceStatus.Paid)
            throw new DomainException("Invoice is already paid.");
        Status = InvoiceStatus.Paid;
        PaidAt = paidAt;
    }

    public bool IsPaid() => Status == InvoiceStatus.Paid;
}
