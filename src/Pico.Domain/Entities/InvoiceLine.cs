namespace Pico.Domain.Entities;

/// <summary>
/// One billable line on an invoice, tied to a resource + flavor for the period.
/// </summary>
public class InvoiceLine
{
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid ResourceId { get; private set; }
    public Guid FlavorId { get; private set; }
    public decimal Hours { get; private set; }
    public decimal Rate { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;

    private InvoiceLine() { }

    public static InvoiceLine Create(
        Guid invoiceId,
        Guid resourceId,
        Guid flavorId,
        decimal hours,
        decimal rate,
        string description)
    {
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice id required.", nameof(invoiceId));
        if (resourceId == Guid.Empty) throw new ArgumentException("Resource id required.", nameof(resourceId));
        if (flavorId == Guid.Empty) throw new ArgumentException("Flavor id required.", nameof(flavorId));
        if (hours <= 0) throw new ArgumentException("Hours must be > 0.", nameof(hours));
        if (rate <= 0) throw new ArgumentException("Rate must be > 0.", nameof(rate));

        return new InvoiceLine
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            ResourceId = resourceId,
            FlavorId = flavorId,
            Hours = hours,
            Rate = rate,
            Amount = decimal.Round(hours * rate, 2, MidpointRounding.AwayFromZero),
            Description = description ?? string.Empty,
        };
    }
}