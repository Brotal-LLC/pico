using Pico.Application.Billing;
using Pico.Domain.Entities;

namespace Pico.Tests.Unit;

public class PricingCalculatorTests
{
    private static Flavor MakeFlavor(decimal perHour, decimal perMonth) =>
        Flavor.Create("test", 1, 1024, 20, perHour, perMonth, "Test");

    [Fact]
    public void HourlyEstimate_ReturnsPerHourRate()
    {
        var flavor = MakeFlavor(perHour: 0.05m, perMonth: 30m);
        var calc = new PricingCalculator();
        Assert.Equal(0.05m, calc.HourlyEstimate(flavor));
    }

    [Fact]
    public void MonthlyEstimate_ReturnsPerMonthRate()
    {
        var flavor = MakeFlavor(perHour: 0.05m, perMonth: 30m);
        var calc = new PricingCalculator();
        Assert.Equal(30m, calc.MonthlyEstimate(flavor));
    }

    [Theory]
    [InlineData(0.05, 720, 36)]
    [InlineData(0.10, 730, 73)]
    [InlineData(0.025, 100, 2.5)]
    public void EstimateForHours_MultipliesRate(double perHour, double hours, double expected)
    {
        var flavor = MakeFlavor((decimal)perHour, 9999m);
        var calc = new PricingCalculator();
        var est = calc.EstimateForHours(flavor, (decimal)hours);
        Assert.Equal((decimal)expected, decimal.Round(est, 4, MidpointRounding.AwayFromZero));
    }

    [Theory]
    [InlineData(0.05, 0)]
    [InlineData(0.05, -1)]
    public void EstimateForHours_ZeroOrNegativeHours_ReturnsZero(double perHour, double hours)
    {
        var flavor = MakeFlavor((decimal)perHour, 9999m);
        var calc = new PricingCalculator();
        Assert.Equal(0m, calc.EstimateForHours(flavor, (decimal)hours));
    }

    [Fact]
    public void EstimateForDays_RoundsToTwoDecimals()
    {
        var flavor = MakeFlavor(perHour: 0.07m, perMonth: 9999m);
        var calc = new PricingCalculator();
        // 3 days × 24 hours = 72 hours × 0.07 = 5.04
        var est = calc.EstimateForDays(flavor, 3);
        Assert.Equal(5.04m, est);
    }
}
