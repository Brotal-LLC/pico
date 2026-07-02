using Microsoft.EntityFrameworkCore;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain.Enums;
using Pico.Infrastructure.Persistence;
using Pico.Infrastructure.Provisioning;
using Pico.Infrastructure.Seed;
using Xunit;

namespace Pico.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="DataSeeder"/> against a real Postgres container.
/// Verifies that a cold-boot seed produces the catalog + demo user + resource + a
/// realistic invoice history (paid historical invoices + one pending current invoice
/// with multiple line items) so reviewers see meaningful billing data on first login.
/// </summary>
[Collection("Postgres")]
public class DataSeederIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;
    private PicoDbContext _db = null!;

    public DataSeederIntegrationTests(PostgresFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.ResetAsync();
        _db = new PicoDbContext(_fx.BuildOptions());
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SeedAsync_ColdBoot_PopulatesCatalogAndUsers()
    {
        var seeder = new DataSeeder(new PasswordHasher(), new MockProvisioningBackend());
        await seeder.SeedAsync(_db, CancellationToken.None);

        // Catalog
        Assert.True(await _db.Flavors.AnyAsync(CancellationToken.None));
        Assert.True(await _db.Images.AnyAsync(CancellationToken.None));

        // Demo users
        var demo = await _db.Users.FirstOrDefaultAsync(u => u.Email == "demo@pico.local", CancellationToken.None);
        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Email == "admin@pico.local", CancellationToken.None);
        Assert.NotNull(demo);
        Assert.NotNull(admin);
        Assert.Equal(UserRole.Customer, demo!.Role);
        Assert.Equal(UserRole.Admin, admin!.Role);

        // Demo resource
        var resource = await _db.Resources.FirstOrDefaultAsync(r => r.UserId == demo.Id, CancellationToken.None);
        Assert.NotNull(resource);
    }

    [Fact]
    public async Task SeedAsync_ColdBoot_ProducesInvoiceHistoryWithMixOfStatuses()
    {
        var seeder = new DataSeeder(new PasswordHasher(), new MockProvisioningBackend());
        await seeder.SeedAsync(_db, CancellationToken.None);

        var demo = await _db.Users.FirstAsync(u => u.Email == "demo@pico.local", CancellationToken.None);
        var invoices = await _db.Invoices
            .Where(i => i.UserId == demo.Id)
            .OrderBy(i => i.PeriodStart)
            .ToListAsync(CancellationToken.None);

        // Three invoices: two historical paid + one current pending
        Assert.Equal(3, invoices.Count);
        Assert.Equal(2, invoices.Count(i => i.Status == InvoiceStatus.Paid));
        Assert.Single(invoices, i => i.Status == InvoiceStatus.Pending);
    }

    [Fact]
    public async Task SeedAsync_ColdBoot_CurrentPendingInvoiceHasMultipleLineItems()
    {
        var seeder = new DataSeeder(new PasswordHasher(), new MockProvisioningBackend());
        await seeder.SeedAsync(_db, CancellationToken.None);

        var demo = await _db.Users.FirstAsync(u => u.Email == "demo@pico.local", CancellationToken.None);
        var pending = await _db.Invoices
            .Include(i => i.Lines)
            .FirstAsync(i => i.UserId == demo.Id && i.Status == InvoiceStatus.Pending, CancellationToken.None);

        // The current pending invoice spans multiple flavors so the detail view
        // can demonstrate a per-flavor breakdown. We expect at least 2 lines.
        Assert.True(pending.Lines.Count >= 2,
            $"Pending invoice should have multiple line items, got {pending.Lines.Count}");

        // All lines have an associated resource and a non-zero amount.
        Assert.All(pending.Lines, line =>
        {
            Assert.NotEqual(Guid.Empty, line.ResourceId);
            Assert.NotEqual(Guid.Empty, line.FlavorId);
            Assert.True(line.Amount > 0, $"Line amount must be > 0, got {line.Amount}");
        });

        // The invoice total is the sum of its line amounts.
        var sum = pending.Lines.Sum(l => l.Amount);
        Assert.Equal(pending.Total, sum);
    }

    [Fact]
    public async Task SeedAsync_ColdBoot_PaidInvoicesHavePaidAtAndZeroPendingTotal()
    {
        var seeder = new DataSeeder(new PasswordHasher(), new MockProvisioningBackend());
        await seeder.SeedAsync(_db, CancellationToken.None);

        var demo = await _db.Users.FirstAsync(u => u.Email == "demo@pico.local", CancellationToken.None);
        var paid = await _db.Invoices
            .Where(i => i.UserId == demo.Id && i.Status == InvoiceStatus.Paid)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(2, paid.Count);
        Assert.All(paid, invoice =>
        {
            Assert.NotNull(invoice.PaidAt);
            Assert.True(invoice.Total > 0);
        });
    }

    [Fact]
    public async Task SeedAsync_CalledTwice_DoesNotDuplicate()
    {
        var seeder = new DataSeeder(new PasswordHasher(), new MockProvisioningBackend());
        await seeder.SeedAsync(_db, CancellationToken.None);
        await seeder.SeedAsync(_db, CancellationToken.None);

        // Idempotent: a second call must not double the invoice count.
        var demo = await _db.Users.FirstAsync(u => u.Email == "demo@pico.local", CancellationToken.None);
        var invoiceCount = await _db.Invoices.CountAsync(i => i.UserId == demo.Id, CancellationToken.None);
        Assert.Equal(3, invoiceCount);
    }
}
