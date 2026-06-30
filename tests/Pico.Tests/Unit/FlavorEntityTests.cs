using Pico.Domain.Entities;

namespace Pico.Tests.Unit;

public class FlavorEntityTests
{
    [Fact]
    public void Create_WithValidArgs_ReturnsFlavor()
    {
        var flavor = Flavor.Create(
            name: "pico.small",
            vcpus: 1,
            ramMb: 1024,
            diskGb: 20,
            pricePerHour: 0.012m,
            pricePerMonth: 7.20m,
            category: "General Purpose");

        Assert.NotEqual(Guid.Empty, flavor.Id);
        Assert.Equal("pico.small", flavor.Name);
        Assert.Equal(1, flavor.Vcpus);
        Assert.Equal(1024, flavor.RamMb);
        Assert.Equal(20, flavor.DiskGb);
        Assert.Equal(0.012m, flavor.PricePerHour);
        Assert.Equal(7.20m, flavor.PricePerMonth);
        Assert.True(flavor.Active);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Create_WithInvalidVcpus_Throws(int vcpus)
    {
        Assert.Throws<ArgumentException>(() =>
            Flavor.Create("name", vcpus, 1024, 20, 0.01m, 1m, "cat"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidRam_Throws(int ram)
    {
        Assert.Throws<ArgumentException>(() =>
            Flavor.Create("name", 1, ram, 20, 0.01m, 1m, "cat"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidDisk_Throws(int disk)
    {
        Assert.Throws<ArgumentException>(() =>
            Flavor.Create("name", 1, 1024, disk, 0.01m, 1m, "cat"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Create_WithNonPositivePrice_Throws(decimal price)
    {
        Assert.Throws<ArgumentException>(() =>
            Flavor.Create("name", 1, 1024, 20, price, 1m, "cat"));
    }

    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Flavor.Create("", 1, 1024, 20, 0.01m, 1m, "cat"));
    }

    [Fact]
    public void Deactivate_MarksInactive()
    {
        var flavor = Flavor.Create("name", 1, 1024, 20, 0.01m, 1m, "cat");
        flavor.Deactivate();
        Assert.False(flavor.Active);
    }

    [Fact]
    public void Activate_MarksActive()
    {
        var flavor = Flavor.Create("name", 1, 1024, 20, 0.01m, 1m, "cat");
        flavor.Deactivate();
        flavor.Activate();
        Assert.True(flavor.Active);
    }

    [Fact]
    public void UpdatePrice_ReplacesPricing()
    {
        var flavor = Flavor.Create("name", 1, 1024, 20, 0.01m, 1m, "cat");
        flavor.UpdatePrice(0.02m, 12m);
        Assert.Equal(0.02m, flavor.PricePerHour);
        Assert.Equal(12m, flavor.PricePerMonth);
    }
}