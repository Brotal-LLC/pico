// Quick prototype of DataSeeder - mark as draft in file content
using Microsoft.EntityFrameworkCore;
using Pico.Domain.Entities;
using Pico.Domain.Enums;

namespace Pico.Infrastructure.Seed;

/// <summary>
/// Idempotent seeder — runs on startup when DemoData__Enabled=true.
/// Populates flavors, images, demo users. Exits silently if data already exists.
/// </summary>
public class DataSeeder
{
    private readonly Persistence.PicoDbContext _db;
    public DataSeeder(Persistence.PicoDbContext db) => _db = db;

    public async Task SeedAsync(CancellationToken ct)
    {
        // Flavors (insert only if empty)
        if (!await _db.Flavors.AnyAsync(ct))
        {
            _db.Flavors.AddRange(
                Flavor.Create("pico.nano", 1, 512, 10, 0.005m, 3.00m, "General Purpose"),
                Flavor.Create("pico.micro", 1, 1024, 20, 0.012m, 7.20m, "General Purpose"),
                Flavor.Create("pico.small", 1, 2048, 40, 0.025m, 15.00m, "General Purpose"),
                Flavor.Create("pico.medium", 2, 4096, 80, 0.050m, 30.00m, "General Purpose"),
                Flavor.Create("pico.large", 4, 8192, 160, 0.100m, 60.00m, "Compute Optimized"),
                Flavor.Create("pico.xlarge", 8, 16384, 320, 0.200m, 120.00m, "Compute Optimized")
            );
            await _db.SaveChangesAsync(ct);
        }

        // Images
        if (!await _db.Images.AnyAsync(ct))
        {
            _db.Images.AddRange(
                Image.Create("ubuntu-24-04", "Ubuntu", "24.04 LTS", 2),
                Image.Create("ubuntu-22-04", "Ubuntu", "22.04 LTS", 2),
                Image.Create("debian-12", "Debian", "12 Bookworm", 2),
                Image.Create("almalinux-9", "AlmaLinux", "9", 3)
            );
            await _db.SaveChangesAsync(ct);
        }

        // Demo users
        if (!await _db.Users.AnyAsync(u => u.Email == "demo@pico.local", ct))
        {
            _db.Users.Add(User.Create(
                email: "demo@pico.local",
                name: "Demo Customer",
                passwordHash: "argon2id$demo$pico-demo-password",
                role: UserRole.Customer));
        }
        if (!await _db.Users.AnyAsync(u => u.Email == "admin@pico.local", ct))
        {
            _db.Users.Add(User.Create(
                email: "admin@pico.local",
                name: "Pico Admin",
                passwordHash: "argon2id$admin$pico-admin-password",
                role: UserRole.Admin));
            await _db.SaveChangesAsync(ct);
        }
    }
}
