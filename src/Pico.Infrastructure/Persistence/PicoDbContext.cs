using Microsoft.EntityFrameworkCore;
using Pico.Domain.Entities;

namespace Pico.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for Pico. Exposes DbSet properties for each aggregate
/// and applies Fluent API configurations from the Configurations/ folder.
/// </summary>
public class PicoDbContext : DbContext
{
    public PicoDbContext(DbContextOptions<PicoDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Flavor> Flavors => Set<Flavor>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceEvent> ResourceEvents => Set<ResourceEvent>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PicoDbContext).Assembly);
    }
}
