using Pico.Domain.Entities;
using Pico.Domain.Enums;

namespace Pico.Tests.Unit;

public class InvoiceEntityTests
{
    private static readonly Guid DefaultUserId = Guid.NewGuid();
    private static readonly Guid DefaultResourceId = Guid.NewGuid();
    private static readonly Guid DefaultFlavorId = Guid.NewGuid();

    [Fact]
    public void Create_WithNoLines_HasZeroTotal()
    {
        var periodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var invoice = Invoice.Create(DefaultUserId, periodStart, periodEnd, Array.Empty<InvoiceLine>());
        Assert.Equal(0m, invoice.Total);
        Assert.Empty(invoice.Lines);
    }

    [Fact]
    public void Create_WithLines_SumsTotal()
    {
        var invoiceId = Guid.NewGuid();
        var line = InvoiceLine.Create(invoiceId, DefaultResourceId, DefaultFlavorId, 24m, 0.05m, "pico.small x 24h");
        var periodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        // InvoiceLine.Create takes invoiceId but invoice.Create re-uses the ID? Let's just sum without strict ID match.
        // The constructor stores lines as-is. We pass a single line.
        var invoice = Invoice.Create(DefaultUserId, periodStart, periodEnd, new[] { line });
        Assert.Equal(1.20m, invoice.Total);
    }

    [Fact]
    public void Create_WithBadPeriod_Throws()
    {
        var t = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() =>
            Invoice.Create(DefaultUserId, t, t, Array.Empty<InvoiceLine>()));
    }

    [Fact]
    public void MarkPaid_SetsStatusAndPaidAt()
    {
        var periodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var invoice = Invoice.Create(DefaultUserId, periodStart, periodEnd, Array.Empty<InvoiceLine>());
        var paidAt = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        invoice.MarkPaid(paidAt);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(paidAt, invoice.PaidAt);
        Assert.True(invoice.IsPaid());
    }

    [Fact]
    public void MarkPaid_Twice_Throws()
    {
        var periodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var invoice = Invoice.Create(DefaultUserId, periodStart, periodEnd, Array.Empty<InvoiceLine>());
        invoice.MarkPaid(DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => invoice.MarkPaid(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NewInvoice_StatusIsPending()
    {
        var periodStart = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var invoice = Invoice.Create(DefaultUserId, periodStart, periodEnd, Array.Empty<InvoiceLine>());
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.False(invoice.IsPaid());
    }
}