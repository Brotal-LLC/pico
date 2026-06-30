using Pico.Domain.Entities;

namespace Pico.Application.Billing;

/// <summary>
/// Pricing logic: per-hour, per-month, per-day estimates from a Flavor.
/// Pure calculator, no IO. Lives in Application to compose with DTOs/invoices later.
/// </summary>
public class PricingCalculator
{
    /// <summary>Hourly cost for a flavor — straight passthrough.</summary>
    public decimal HourlyEstimate(Flavor flavor) => flavor.PricePerHour;

    /// <summary>Monthly cost — straight passthrough (flavor encodes monthly rate).</summary>
    public decimal MonthlyEstimate(Flavor flavor) => flavor.PricePerMonth;

    /// <summary>Cost for a given number of hours. Zero or negative hours → 0. Rounded to 4 decimal places.</summary>
    public decimal EstimateForHours(Flavor flavor, decimal hours)
    {
        if (hours <= 0) return 0m;
        return decimal.Round(flavor.PricePerHour * hours, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>Cost for a given number of days (treating 1 day = 24 hours). Rounded to 2 decimal places.</summary>
    public decimal EstimateForDays(Flavor flavor, int days)
    {
        if (days <= 0) return 0m;
        return decimal.Round(flavor.PricePerHour * 24m * days, 2, MidpointRounding.AwayFromZero);
    }
}
