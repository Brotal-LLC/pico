using Microsoft.EntityFrameworkCore;
using Pico.Application.Common;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Infrastructure.Persistence;

namespace Pico.Infrastructure.Seed;

/// <summary>
/// Seeds the database with initial catalog data (flavors, images)
/// and demo users for the reviewer experience.
/// </summary>
public class DataSeeder
{
    private readonly IPasswordHasher _hasher;

    public DataSeeder(IPasswordHasher hasher)
    {
        _hasher = hasher;
    }

    /// <summary>
    /// Seed catalog data and demo users. Idempotent — skips only if flavors AND
    /// invoices are already populated. Calling this on an existing DB will backfill
    /// missing demo data (e.g. an older DB that pre-dates the historical-invoice seed).
    /// </summary>
    public async Task SeedAsync(PicoDbContext db, CancellationToken ct)
    {
        // Decide what needs backfilling. The `Flavors` check short-circuits the
        // common case (everything already in place). The secondary checks let a
        // reviewer run add what they need after we've added new seed types.
        var flavorsAlready = await db.Flavors.AnyAsync(ct);
        var usersAlready   = await db.Users.AnyAsync(ct);
        var invoiceAlready = await db.Invoices.AnyAsync(ct);
        var resAlready     = await db.Resources.AnyAsync(ct);

        // ─── Flavors ─────────────────────────────────────────────────────
        Flavor[] flavors;
        if (flavorsAlready)
        {
            flavors = await db.Flavors.ToArrayAsync(ct);
        }
        else
        {
            flavors = new[]
            {
                Flavor.Create("pico.nano",  1,  512,  10, 0.005m,  3.0m, "General"),
                Flavor.Create("pico.micro", 1, 1024,  20, 0.010m,  6.0m, "General"),
                Flavor.Create("pico.small", 1, 2048,  40, 0.025m, 15.0m, "General"),
                Flavor.Create("pico.medium",2, 4096,  80, 0.050m, 30.0m, "Compute"),
                Flavor.Create("pico.large", 4, 8192, 160, 0.100m, 60.0m, "Compute"),
                Flavor.Create("pico.xlarge",8,16384, 320, 0.200m,120.0m, "Memory"),
            };
            db.Flavors.AddRange(flavors);
        }

        // ─── Images ──────────────────────────────────────────────────────
        Image[] images;
        if (await db.Images.AnyAsync(ct))
        {
            images = await db.Images.ToArrayAsync(ct);
        }
        else
        {
            images = new[]
            {
                Image.Create("ubuntu-22", "Ubuntu",   "22.04 LTS", 2),
                Image.Create("ubuntu-24", "Ubuntu",   "24.04 LTS", 2),
                Image.Create("debian-12", "Debian",   "12 (Bookworm)", 2),
                Image.Create("alma-9",    "AlmaLinux","9",          3),
            };
            db.Images.AddRange(images);
        }

        // ─── Demo users (passwords hashed with IPasswordHasher) ──────────
        User demoUser, adminUser;
        if (usersAlready)
        {
            demoUser = await db.Users.FirstAsync(u => u.Email == "demo@pico.local", ct);
            adminUser = await db.Users.FirstAsync(u => u.Email == "admin@pico.local", ct);
        }
        else
        {
            var demoHash  = _hasher.Hash("pico-demo-password");
            var adminHash = _hasher.Hash("pico-admin-password");
            demoUser  = User.Create("demo@pico.local",  "Demo User", demoHash,  UserRole.Customer);
            adminUser = User.Create("admin@pico.local", "Admin User", adminHash, UserRole.Admin);
            db.Users.AddRange(demoUser, adminUser);
        }

        await db.SaveChangesAsync(ct);

        // ─── Demo resource (small VM owned by demo) ──────────────────────
        var picoSmall = flavors.First(f => f.Name == "pico.small");
        var ubuntu22  = images.First(i => i.Name == "ubuntu-22");
        Resource demoResource;
        if (resAlready)
        {
            demoResource = await db.Resources
                .FirstAsync(r => r.UserId == demoUser.Id, ct);
        }
        else
        {
            demoResource = Resource.Provision(
                demoUser.Id, picoSmall.Id, ubuntu22.Id, "demo-vm-01");
            db.Resources.Add(demoResource);
            await db.SaveChangesAsync(ct);
        }

        // ─── Historical invoices so reviewers see a real billing history ─
        // Three invoices give the dashboard something meaningful to render:
        //   • Two paid invoices (60d ago, 30d ago) — show historical billing
        //   • One current pending invoice with multiple line items — exercises
        //     the detail view, the "Pay now" CTA, and the per-flavor breakdown.
        if (!invoiceAlready)
        {
            var picoNano   = flavors.First(f => f.Name == "pico.nano");
            var picoMicro  = flavors.First(f => f.Name == "pico.micro");
            var picoMedium = flavors.First(f => f.Name == "pico.medium");
            var now = DateTimeOffset.UtcNow;

            // 60 days ago: paid invoice — single line, pico.medium for a full month
            SeedInvoice(
                db,
                demoUser.Id,
                demoResource.Id,
                picoMedium.Id,
                picoMedium.PricePerHour,
                hours: 720m, // 30d × 24h
                periodStart: now.AddDays(-90),
                periodEnd:   now.AddDays(-60),
                paidAt:      now.AddDays(-59),
                description: $"{demoResource.Name} ({picoMedium.Name})");

            // 30 days ago: paid invoice — single line, pico.small for a week
            SeedInvoice(
                db,
                demoUser.Id,
                demoResource.Id,
                picoSmall.Id,
                picoSmall.PricePerHour,
                hours: 168m, // a week
                periodStart: now.AddDays(-37),
                periodEnd:   now.AddDays(-30),
                paidAt:      now.AddDays(-29),
                description: $"{demoResource.Name} ({picoSmall.Name})");

            // Current period: pending invoice — multiple lines spanning different flavors.
            // Built by hand here (not via the helper) because it's the one with line variety.
            var currentStart = now.AddDays(-7);
            var currentEnd   = now.AddDays(23); // closes in 23 days
            var currentLines = new[]
            {
                BuildLine(demoResource.Id, picoSmall.Id, 96m,  picoSmall.PricePerHour,  $"{demoResource.Name} ({picoSmall.Name})"),
                BuildLine(demoResource.Id, picoNano.Id,  40m,  picoNano.PricePerHour,   $"{demoResource.Name} ({picoNano.Name})"),
                BuildLine(demoResource.Id, picoMicro.Id, 24m,  picoMicro.PricePerHour,  $"{demoResource.Name} ({picoMicro.Name})"),
            };
            var current = Invoice.Create(demoUser.Id, currentStart, currentEnd, currentLines);
            // Pending — no MarkPaid call
            db.Invoices.Add(current);

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Helper: build an InvoiceLine for the seeder. Uses the internal ctor so the
    /// amount can be pre-computed and the invoiceId left at Guid.Empty (EF fixup).
    /// </summary>
    private static InvoiceLine BuildLine(
        Guid resourceId,
        Guid flavorId,
        decimal hours,
        decimal rate,
        string description)
    {
        var amount = decimal.Round(hours * rate, 2, MidpointRounding.AwayFromZero);
        return new InvoiceLine(
            invoiceId: Guid.Empty, // EF Core fixup via Invoice.Lines navigation
            resourceId: resourceId,
            flavorId: flavorId,
            hours: hours,
            rate: rate,
            amount: amount,
            description: description);
    }

    /// <summary>
    /// Helper: persist a single-line invoice and immediately mark it paid.
    /// Used for historical paid invoices in the seed.
    /// </summary>
    private static void SeedInvoice(
        PicoDbContext db,
        Guid userId,
        Guid resourceId,
        Guid flavorId,
        decimal rate,
        decimal hours,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        DateTimeOffset paidAt,
        string description)
    {
        var line = BuildLine(resourceId, flavorId, hours, rate, description);
        var invoice = Invoice.Create(userId, periodStart, periodEnd, new[] { line });
        invoice.MarkPaid(paidAt);
        db.Invoices.Add(invoice);
    }
}