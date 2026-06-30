using Pico.Domain.Entities;

namespace Pico.Application.Billing;

/// <summary>
/// Input to InvoiceGenerator: which resource ran for how many hours in the period.
/// Pre-computed by Application — generator doesn't know about IProvisioningBackend.
/// </summary>
public record ResourceUsageLine(
    Resource Resource,
    Flavor Flavor,
    decimal HoursInPeriod,
    string Description
);