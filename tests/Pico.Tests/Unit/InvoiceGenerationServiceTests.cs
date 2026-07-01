using Pico.Application.Billing;
using Pico.Application.Common;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// Tests for InvoiceGenerationService orchestration logic.
/// Covers: empty user list, no-resource users, normal generation, period clamping.
/// </summary>
public class InvoiceGenerationServiceTests
{
    [Fact]
    public async Task GenerateForPeriodAsync_NoUsers_ReturnsZero()
    {
        var users = new InMemoryUserRepo();
        var flavors = new InMemoryFlavorRepo();
        var resources = new InMemoryResourceRepo();
        var invoices = new InMemoryInvoiceRepo();
        var sut = new InvoiceGenerationService(users, flavors, resources, invoices, new InvoiceGenerator());

        var created = await sut.GenerateForPeriodAsync(
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow);

        Assert.Equal(0, created);
        Assert.Empty(invoices.Added);
    }

    [Fact]
    public async Task GenerateForPeriodAsync_UserWithoutResources_ReturnsZero()
    {
        var users = new InMemoryUserRepo();
        var user = User.Create("test@x.com", "Test", "hash", UserRole.Customer);
        typeof(User).GetProperty("Id")!.SetValue(user, Guid.NewGuid());
        users.Users.Add(user);

        var flavors = new InMemoryFlavorRepo();
        flavors.Flavors.Add(Flavor.Create("pico.nano", 1, 512, 10, 0.01m, 7.2m, "General"));

        var resources = new InMemoryResourceRepo();
        var invoices = new InMemoryInvoiceRepo();
        var sut = new InvoiceGenerationService(users, flavors, resources, invoices, new InvoiceGenerator());

        var created = await sut.GenerateForPeriodAsync(
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow);

        Assert.Equal(0, created);
        Assert.Empty(invoices.Added);
    }

    [Fact]
    public async Task GenerateForPeriodAsync_NormalUser_CreatesOneInvoice()
    {
        var users = new InMemoryUserRepo();
        var user = User.Create("test@x.com", "Test", "hash", UserRole.Customer);
        typeof(User).GetProperty("Id")!.SetValue(user, Guid.NewGuid());
        users.Users.Add(user);

        var flavors = new InMemoryFlavorRepo();
        var nano = Flavor.Create("pico.nano", 1, 512, 10, 0.01m, 7.2m, "General");
        flavors.Flavors.Add(nano);

        var resources = new InMemoryResourceRepo();
        var imageId = Guid.NewGuid();
        var resource = Resource.Provision(user.Id, nano.Id, imageId, "test-vm");
        // Backdate creation so it falls within the period (default CreatedAt is now)
        typeof(Resource).GetProperty("CreatedAt")!.SetValue(resource, DateTimeOffset.UtcNow.AddDays(-5));
        await resources.AddAsync(resource, default);

        var invoices = new InMemoryInvoiceRepo();
        var sut = new InvoiceGenerationService(users, flavors, resources, invoices, new InvoiceGenerator());

        var periodStart = DateTimeOffset.UtcNow.AddDays(-7);
        var periodEnd = DateTimeOffset.UtcNow;
        var created = await sut.GenerateForPeriodAsync(periodStart, periodEnd);

        Assert.Equal(1, created);
        Assert.Single(invoices.Added);
        var invoice = invoices.Added[0];
        Assert.Equal(user.Id, invoice.UserId);
        Assert.Single(invoice.Lines);
        Assert.True(invoice.Total > 0);
    }

    [Fact]
    public async Task GenerateForPeriodAsync_PeriodInversion_Throws()
    {
        var users = new InMemoryUserRepo();
        var flavors = new InMemoryFlavorRepo();
        var resources = new InMemoryResourceRepo();
        var invoices = new InMemoryInvoiceRepo();
        var sut = new InvoiceGenerationService(users, flavors, resources, invoices, new InvoiceGenerator());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await sut.GenerateForPeriodAsync(
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(-1)));
    }

    // ─── Minimal in-memory fakes for this test only ──────────────────────
    private sealed class InMemoryUserRepo : IUserRepository
    {
        public List<User> Users { get; } = new();
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<User?>(Users.FirstOrDefault(u => u.Id == id));
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult<User?>(Users.FirstOrDefault(u => u.Email == email));
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult(Users.Any(u => u.Email == email));
        public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<User>>(Users.ToList());
        public Task AddAsync(User user, CancellationToken ct) { Users.Add(user); return Task.CompletedTask; }
    }

    private sealed class InMemoryFlavorRepo : IFlavorRepository
    {
        public List<Flavor> Flavors { get; } = new();
        public Task<Flavor?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Flavor?>(Flavors.FirstOrDefault(f => f.Id == id));
        public Task<IReadOnlyList<Flavor>> ListActiveAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Flavor>>(Flavors.Where(f => f.Active).ToList());
        public Task<IReadOnlyList<Flavor>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Flavor>>(Flavors.ToList());
        public Task AddAsync(Flavor flavor, CancellationToken ct) { Flavors.Add(flavor); return Task.CompletedTask; }
    }

    private sealed class InMemoryResourceRepo : IResourceRepository
    {
        public Dictionary<Guid, Resource> Resources { get; } = new();
        public Task<Resource?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Resource?>(Resources.GetValueOrDefault(id));
        public Task<IReadOnlyList<Resource>> ListByUserAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Resource>>(Resources.Values.Where(r => r.UserId == userId).ToList());
        public Task<IReadOnlyList<Resource>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Resource>>(Resources.Values.ToList());
        public Task<IReadOnlyList<Resource>> ListActiveByUserAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Resource>>(Resources.Values.Where(r => r.UserId == userId && !r.IsTerminated()).ToList());
        public Task AddAsync(Resource resource, CancellationToken ct) { Resources[resource.Id] = resource; return Task.CompletedTask; }
        public Task UpdateAsync(Resource resource, CancellationToken ct) { Resources[resource.Id] = resource; return Task.CompletedTask; }
        public Task AddEventAsync(ResourceEvent evt, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ResourceEvent>> ListEventsAsync(Guid resourceId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ResourceEvent>>(Array.Empty<ResourceEvent>());
    }

    private sealed class InMemoryInvoiceRepo : IInvoiceRepository
    {
        public List<Invoice> Added { get; } = new();
        public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Invoice?>(Added.FirstOrDefault(i => i.Id == id));
        public Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Invoice>>(Added.Where(i => i.UserId == userId).ToList());
        public Task<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Invoice>>(Added.ToList());
        public Task AddAsync(Invoice invoice, CancellationToken ct) { Added.Add(invoice); return Task.CompletedTask; }
        public Task UpdateAsync(Invoice invoice, CancellationToken ct) { return Task.CompletedTask; }
    }
}