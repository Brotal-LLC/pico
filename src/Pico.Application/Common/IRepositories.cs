using Pico.Domain.Entities;

namespace Pico.Application.Common;

/// <summary>
/// Repository contracts for Application layer. Implementations live in Infrastructure
/// using EF Core. Application depends on these interfaces; Infrastructure implements them.
/// </summary>
public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<User?> FindByEmailAsync(string email, CancellationToken ct);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct);
    Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
}

public interface IFlavorRepository
{
    Task<Flavor?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Flavor>> ListActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<Flavor>> ListAllAsync(CancellationToken ct);
    Task AddAsync(Flavor flavor, CancellationToken ct);
}

public interface IImageRepository
{
    Task<Image?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Image>> ListActiveAsync(CancellationToken ct);
    Task AddAsync(Image image, CancellationToken ct);
}

public interface IResourceRepository
{
    Task<Resource?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Resource>> ListByUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<Resource>> ListAllAsync(CancellationToken ct);
    Task<IReadOnlyList<Resource>> ListActiveByUserAsync(Guid userId, CancellationToken ct);
    Task AddAsync(Resource resource, CancellationToken ct);
    Task UpdateAsync(Resource resource, CancellationToken ct);

    /// <summary>Append-only log of state transitions for the SSE feed.</summary>
    Task AddEventAsync(ResourceEvent evt, CancellationToken ct);
    Task<IReadOnlyList<ResourceEvent>> ListEventsAsync(Guid resourceId, CancellationToken ct);
}

public interface IInvoiceRepository
{
    Task<Invoice?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct);
    Task AddAsync(Invoice invoice, CancellationToken ct);
    Task UpdateAsync(Invoice invoice, CancellationToken ct);
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct);
    Task<IReadOnlyList<AuditLog>> ListAsync(DateTimeOffset since, CancellationToken ct);
}
