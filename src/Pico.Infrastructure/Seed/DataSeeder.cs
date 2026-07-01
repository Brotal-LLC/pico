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

    /// <summary>Seed catalog data and demo users. Idempotent — skips if flavors exist.</summary>
    public async Task SeedAsync(PicoDbContext db, CancellationToken ct)
    {
        if (await db.Flavors.AnyAsync(ct)) return;

        // ─── Flavors ─────────────────────────────────────────────────────
        var flavors = new[]
        {
            Flavor.Create("pico.nano",  1,  512,  10, 0.005m,  3.0m, "General"),
            Flavor.Create("pico.micro", 1, 1024,  20, 0.010m,  6.0m, "General"),
            Flavor.Create("pico.small", 1, 2048,  40, 0.025m, 15.0m, "General"),
            Flavor.Create("pico.medium",2, 4096,  80, 0.050m, 30.0m, "Compute"),
            Flavor.Create("pico.large", 4, 8192, 160, 0.100m, 60.0m, "Compute"),
            Flavor.Create("pico.xlarge",8,16384, 320, 0.200m,120.0m, "Memory"),
        };
        db.Flavors.AddRange(flavors);

        // ─── Images ──────────────────────────────────────────────────────
        var images = new[]
        {
            Image.Create("ubuntu-22", "Ubuntu",   "22.04 LTS", 2),
            Image.Create("ubuntu-24", "Ubuntu",   "24.04 LTS", 2),
            Image.Create("debian-12", "Debian",   "12 (Bookworm)", 2),
            Image.Create("alma-9",    "AlmaLinux","9",          3),
        };
        db.Images.AddRange(images);

        // ─── Demo users (passwords hashed with IPasswordHasher) ──────────
        var demoHash = _hasher.Hash("pico-demo-password");
        var adminHash = _hasher.Hash("pico-admin-password");

        var demoUser = User.Create("demo@pico.local", "Demo User", demoHash, UserRole.Customer);
        var adminUser = User.Create("admin@pico.local", "Admin User", adminHash, UserRole.Admin);
        db.Users.AddRange(demoUser, adminUser);

        await db.SaveChangesAsync(ct);

        // ─── Demo resource + historical invoice so reviewers see real data ─
        // Creates one small VM owned by demo, then a paid invoice for the prior month.
        var picoSmall = flavors.First(f => f.Name == "pico.small");
        var ubuntu22 = images.First(i => i.Name == "ubuntu-22");
        var demoResource = Resource.Provision(
            demoUser.Id, picoSmall.Id, ubuntu22.Id, "demo-vm-01");
        db.Resources.Add(demoResource);

        var lastMonth = DateTimeOffset.UtcNow.AddDays(-30);
        var lastWeek = DateTimeOffset.UtcNow.AddDays(-7);
        var hours = 168m; // a week
        var line = new InvoiceLine(
            invoiceId: Guid.Empty,
            resourceId: demoResource.Id,
            flavorId: picoSmall.Id,
            hours: hours,
            rate: picoSmall.PricePerHour,
            amount: decimal.Round(picoSmall.PricePerHour * hours, 2, MidpointRounding.AwayFromZero),
            description: $"{demoResource.Name} ({picoSmall.Name})");
        var historical = Invoice.Create(demoUser.Id, lastMonth, lastWeek, new[] { line });
        historical.MarkPaid(lastWeek.AddDays(1));
        db.Invoices.Add(historical);

        await db.SaveChangesAsync(ct);
    }
}