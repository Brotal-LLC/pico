using Pico.Application.Billing;
using Pico.Application.Common;
using Pico.Domain.Entities;
using Pico.Domain.Enums;

namespace Pico.Application.Billing;

/// <summary>
/// Orchestrates invoice generation for all users in a billing period.
/// Reads resource state, computes usage lines per user, runs InvoiceGenerator,
/// and persists resulting invoices. Pure orchestration — no HTTP concerns.
/// </summary>
public class InvoiceGenerationService
{
    private readonly IUserRepository _users;
    private readonly IFlavorRepository _flavors;
    private readonly IResourceRepository _resources;
    private readonly IInvoiceRepository _invoices;
    private readonly InvoiceGenerator _generator;

    public InvoiceGenerationService(
        IUserRepository users,
        IFlavorRepository flavors,
        IResourceRepository resources,
        IInvoiceRepository invoices,
        InvoiceGenerator generator)
    {
        _users = users;
        _flavors = flavors;
        _resources = resources;
        _invoices = invoices;
        _generator = generator;
    }

    /// <summary>
    /// Generate and persist invoices for every user with billable activity in the
    /// given period. Returns the number of invoices created.
    /// </summary>
    public async Task<int> GenerateForPeriodAsync(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(periodStart, periodEnd);

        var users = await _users.ListAllAsync(ct);
        var flavorsById = (await _flavors.ListAllAsync(ct)).ToDictionary(f => f.Id);

        int created = 0;
        foreach (var user in users)
        {
            var resources = await _resources.ListByUserAsync(user.Id, ct);
            // Skip users without any resources
            if (resources.Count == 0) continue;

            var lines = new List<ResourceUsageLine>();
            foreach (var resource in resources)
            {
                if (resource.Status == ResourceStatus.Terminated) continue;
                if (!flavorsById.TryGetValue(resource.FlavorId, out var flavor)) continue;

                // Compute hours the resource was alive during the period.
                var hours = ComputeHoursInPeriod(resource, periodStart, periodEnd);
                if (hours <= 0) continue;

                lines.Add(new ResourceUsageLine(
                    resource,
                    flavor,
                    hours,
                    $"{resource.Name} ({flavor.Name})"));
            }

            var invoice = _generator.Generate(user.Id, periodStart, periodEnd, lines);
            if (invoice is null) continue;

            await _invoices.AddAsync(invoice, ct);
            created++;
        }
        return created;
    }

    private static decimal ComputeHoursInPeriod(
        Resource resource, DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        var resourceStart = resource.CreatedAt;
        var resourceEnd = resource.UpdatedAt;

        // If the resource was updated after the period, treat it as still alive.
        // The estimate is coarse but consistent with "estimated usage" semantics.
        if (resourceEnd < periodStart) return 0;
        if (resourceStart > periodEnd) return 0;

        var effectiveStart = resourceStart > periodStart ? resourceStart : periodStart;
        var effectiveEnd = resourceEnd < periodEnd ? resourceEnd : periodEnd;

        var hours = (decimal)(effectiveEnd - effectiveStart).TotalHours;
        return hours < 0 ? 0 : hours;
    }
}