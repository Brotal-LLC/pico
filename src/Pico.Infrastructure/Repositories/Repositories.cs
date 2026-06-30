using Microsoft.EntityFrameworkCore;
using Pico.Application.Common;
using Pico.Domain.Entities;

namespace Pico.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly Persistence.PicoDbContext _db;
    public UserRepository(Persistence.PicoDbContext db) => _db = db;

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct) =>
        _db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task AddAsync(User user, CancellationToken ct)
    {
        await _db.Users.AddAsync(user, ct);
        await _db.SaveChangesAsync(ct);
    }
}

public class FlavorRepository : IFlavorRepository
{
    private readonly Persistence.PicoDbContext _db;
    public FlavorRepository(Persistence.PicoDbContext db) => _db = db;

    public Task<Flavor?> FindByIdAsync(Guid id, CancellationToken ct) =>
        _db.Flavors.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IReadOnlyList<Flavor>> ListActiveAsync(CancellationToken ct) =>
        await _db.Flavors.Where(f => f.Active).OrderBy(f => f.PricePerHour).ToListAsync(ct);

    public async Task<IReadOnlyList<Flavor>> ListAllAsync(CancellationToken ct) =>
        await _db.Flavors.OrderBy(f => f.PricePerHour).ToListAsync(ct);

    public async Task AddAsync(Flavor flavor, CancellationToken ct)
    {
        await _db.Flavors.AddAsync(flavor, ct);
        await _db.SaveChangesAsync(ct);
    }
}

public class ImageRepository : IImageRepository
{
    private readonly Persistence.PicoDbContext _db;
    public ImageRepository(Persistence.PicoDbContext db) => _db = db;

    public Task<Image?> FindByIdAsync(Guid id, CancellationToken ct) =>
        _db.Images.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<Image>> ListActiveAsync(CancellationToken ct) =>
        await _db.Images.Where(i => i.Active).OrderBy(i => i.Name).ToListAsync(ct);

    public async Task AddAsync(Image image, CancellationToken ct)
    {
        await _db.Images.AddAsync(image, ct);
        await _db.SaveChangesAsync(ct);
    }
}

public class ResourceRepository : IResourceRepository
{
    private readonly Persistence.PicoDbContext _db;
    public ResourceRepository(Persistence.PicoDbContext db) => _db = db;

    public Task<Resource?> FindByIdAsync(Guid id, CancellationToken ct) =>
        _db.Resources.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<Resource>> ListByUserAsync(Guid userId, CancellationToken ct) =>
        await _db.Resources.Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Resource>> ListAllAsync(CancellationToken ct) =>
        await _db.Resources.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Resource>> ListActiveByUserAsync(Guid userId, CancellationToken ct) =>
        await _db.Resources
            .Where(r => r.UserId == userId && r.Status != Domain.Enums.ResourceStatus.Terminated)
            .OrderByDescending(r => r.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(Resource resource, CancellationToken ct)
    {
        await _db.Resources.AddAsync(resource, ct);
        await _db.SaveChangesAsync(ct);
    }

    public void Update(Resource resource)
    {
        _db.Resources.Update(resource);
        _db.SaveChanges();
    }

    public async Task AddEventAsync(ResourceEvent evt, CancellationToken ct)
    {
        await _db.ResourceEvents.AddAsync(evt, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ResourceEvent>> ListEventsAsync(Guid resourceId, CancellationToken ct) =>
        await _db.ResourceEvents
            .Where(e => e.ResourceId == resourceId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(ct);
}

public class InvoiceRepository : IInvoiceRepository
{
    private readonly Persistence.PicoDbContext _db;
    public InvoiceRepository(Persistence.PicoDbContext db) => _db = db;

    public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken ct) =>
        _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken ct) =>
        await _db.Invoices.Where(i => i.UserId == userId).OrderByDescending(i => i.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct) =>
        await _db.Invoices.OrderByDescending(i => i.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(Invoice invoice, CancellationToken ct)
    {
        await _db.Invoices.AddAsync(invoice, ct);
        await _db.SaveChangesAsync(ct);
    }

    public void Update(Invoice invoice)
    {
        _db.Invoices.Update(invoice);
        _db.SaveChanges();
    }
}

public class AuditLogRepository : IAuditLogRepository
{
    private readonly Persistence.PicoDbContext _db;
    public AuditLogRepository(Persistence.PicoDbContext db) => _db = db;

    public async Task AddAsync(AuditLog log, CancellationToken ct)
    {
        await _db.AuditLogs.AddAsync(log, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLog>> ListAsync(DateTimeOffset since, CancellationToken ct) =>
        await _db.AuditLogs.Where(l => l.Timestamp >= since).OrderByDescending(l => l.Timestamp).ToListAsync(ct);
}
