using Pico.Application.Billing;
using Pico.Domain.Entities;

namespace Pico.Tests.Unit;

public class InvoiceGeneratorTests
{
    private static readonly Guid DefaultUserId = Guid.NewGuid();
    private static readonly DateTimeOffset PeriodStart = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_NoUsage_ReturnsNull()
    {
        var gen = new InvoiceGenerator();
        var invoice = gen.Generate(DefaultUserId, PeriodStart, PeriodEnd, Array.Empty<ResourceUsageLine>());
        Assert.Null(invoice);
    }

    [Fact]
    public void Generate_OneResource_Hours_CreatesLine()
    {
        var flavor = Flavor.Create("pico.small", 1, 1024, 20, 0.05m, 30m, "General");
        var resource = Resource.Provision(DefaultUserId, flavor.Id, Guid.NewGuid(), "my-vm");
        var usage = new List<ResourceUsageLine>
        {
            new(resource, flavor, HoursInPeriod: 720m, Description: "pico.small × 720h")
        };

        var gen = new InvoiceGenerator();
        var invoice = gen.Generate(DefaultUserId, PeriodStart, PeriodEnd, usage);

        Assert.NotNull(invoice);
        Assert.Single(invoice.Lines);
        Assert.Equal(720m * 0.05m, invoice.Total);  // 36.00
        Assert.Equal(720m, invoice.Lines[0].Hours);
        Assert.Equal(resource.Id, invoice.Lines[0].ResourceId);
    }

    [Fact]
    public void Generate_MultipleResources_SumsTotal()
    {
        var f1 = Flavor.Create("pico.small", 1, 1024, 20, 0.05m, 30m, "General");
        var f2 = Flavor.Create("pico.medium", 2, 2048, 40, 0.10m, 60m, "General");
        var r1 = Resource.Provision(DefaultUserId, f1.Id, Guid.NewGuid(), "vm1");
        var r2 = Resource.Provision(DefaultUserId, f2.Id, Guid.NewGuid(), "vm2");

        var usage = new List<ResourceUsageLine>
        {
            new(r1, f1, 720m, "small x 720h"),
            new(r2, f2, 360m, "medium x 360h"),  // 360 * 0.10 = 36.00
        };

        var gen = new InvoiceGenerator();
        var invoice = gen.Generate(DefaultUserId, PeriodStart, PeriodEnd, usage);

        Assert.NotNull(invoice);
        Assert.Equal(2, invoice.Lines.Count);
        // Total: 720*0.05 + 360*0.10 = 36 + 36 = 72.00
        Assert.Equal(72m, invoice.Total);
    }

    [Fact]
    public void Generate_LineWithZeroHours_SkipsLine()
    {
        var flavor = Flavor.Create("pico.small", 1, 1024, 20, 0.05m, 30m, "General");
        var resource = Resource.Provision(DefaultUserId, flavor.Id, Guid.NewGuid(), "vm");
        var usage = new List<ResourceUsageLine>
        {
            new(resource, flavor, HoursInPeriod: 0m, Description: "idle"),
        };

        var gen = new InvoiceGenerator();
        var invoice = gen.Generate(DefaultUserId, PeriodStart, PeriodEnd, usage);
        Assert.Null(invoice);  // No billable items → no invoice
    }

    [Fact]
    public void Generate_LineWithNegativeHours_SkipsLine()
    {
        var flavor = Flavor.Create("pico.small", 1, 1024, 20, 0.05m, 30m, "General");
        var resource = Resource.Provision(DefaultUserId, flavor.Id, Guid.NewGuid(), "vm");
        var usage = new List<ResourceUsageLine>
        {
            new(resource, flavor, HoursInPeriod: -5m, Description: "negative"),
        };

        var gen = new InvoiceGenerator();
        var invoice = gen.Generate(DefaultUserId, PeriodStart, PeriodEnd, usage);
        Assert.Null(invoice);
    }
}
