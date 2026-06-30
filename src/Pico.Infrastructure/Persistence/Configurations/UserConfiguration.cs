using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pico.Domain.Entities;

namespace Pico.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).HasColumnName("id");
        b.Property(u => u.Email).HasColumnName("email").IsRequired().HasMaxLength(255);
        b.Property(u => u.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        b.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired().HasMaxLength(255);
        b.Property(u => u.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
        b.Property(u => u.CreatedAt).HasColumnName("created_at");

        b.HasIndex(u => u.Email).IsUnique();
    }
}

public class FlavorConfiguration : IEntityTypeConfiguration<Flavor>
{
    public void Configure(EntityTypeBuilder<Flavor> b)
    {
        b.ToTable("flavors");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        b.Property(f => f.Vcpus).HasColumnName("vcpus");
        b.Property(f => f.RamMb).HasColumnName("ram_mb");
        b.Property(f => f.DiskGb).HasColumnName("disk_gb");
        b.Property(f => f.PricePerHour).HasColumnName("price_per_hour").HasColumnType("numeric(12,4)");
        b.Property(f => f.PricePerMonth).HasColumnName("price_per_month").HasColumnType("numeric(12,2)");
        b.Property(f => f.Category).HasColumnName("category").HasMaxLength(50);
        b.Property(f => f.Active).HasColumnName("active");

        b.HasIndex(f => f.Name).IsUnique();
    }
}

public class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public void Configure(EntityTypeBuilder<Image> b)
    {
        b.ToTable("images");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id");
        b.Property(i => i.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        b.Property(i => i.Os).HasColumnName("os").HasMaxLength(50);
        b.Property(i => i.Version).HasColumnName("version").HasMaxLength(50);
        b.Property(i => i.SizeGb).HasColumnName("size_gb");
        b.Property(i => i.Active).HasColumnName("active");

        b.HasIndex(i => i.Name).IsUnique();
    }
}

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> b)
    {
        b.ToTable("resources");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.UserId).HasColumnName("user_id");
        b.Property(r => r.FlavorId).HasColumnName("flavor_id");
        b.Property(r => r.ImageId).HasColumnName("image_id");
        b.Property(r => r.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        b.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.ExternalId).HasColumnName("external_id").HasMaxLength(255);
        b.Property(r => r.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
        b.Property(r => r.CreatedAt).HasColumnName("created_at");
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        b.HasIndex(r => r.UserId);
        b.HasIndex(r => r.ExternalId);

        b.HasOne<Flavor>()
            .WithMany()
            .HasForeignKey(r => r.FlavorId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Image>()
            .WithMany()
            .HasForeignKey(r => r.ImageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ResourceEventConfiguration : IEntityTypeConfiguration<ResourceEvent>
{
    public void Configure(EntityTypeBuilder<ResourceEvent> b)
    {
        b.ToTable("resource_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.ResourceId).HasColumnName("resource_id");
        b.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(50);
        b.Property(e => e.OldStatus).HasColumnName("old_status").HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.NewStatus).HasColumnName("new_status").HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Message).HasColumnName("message").HasMaxLength(500);
        b.Property(e => e.Timestamp).HasColumnName("timestamp");

        b.HasIndex(e => new { e.ResourceId, e.Timestamp });
    }
}

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id");
        b.Property(i => i.UserId).HasColumnName("user_id");
        b.Property(i => i.PeriodStart).HasColumnName("period_start");
        b.Property(i => i.PeriodEnd).HasColumnName("period_end");
        b.Property(i => i.Total).HasColumnName("total").HasColumnType("numeric(12,2)");
        b.Property(i => i.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.CreatedAt).HasColumnName("created_at");
        b.Property(i => i.PaidAt).HasColumnName("paid_at");

        b.HasIndex(i => i.UserId);

        // Map navigation property — EF Core will use the backing field automatically
        b.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey("InvoiceId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable("invoice_lines");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).HasColumnName("id");
        b.Property(l => l.InvoiceId).HasColumnName("invoice_id");
        b.Property(l => l.ResourceId).HasColumnName("resource_id");
        b.Property(l => l.FlavorId).HasColumnName("flavor_id");
        b.Property(l => l.Hours).HasColumnName("hours").HasColumnType("numeric(12,4)");
        b.Property(l => l.Rate).HasColumnName("rate").HasColumnType("numeric(12,4)");
        b.Property(l => l.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        b.Property(l => l.Description).HasColumnName("description").HasMaxLength(255);

        b.HasIndex(l => l.InvoiceId);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).HasColumnName("id");
        b.Property(l => l.UserId).HasColumnName("user_id");
        b.Property(l => l.Action).HasColumnName("action").HasMaxLength(100);
        b.Property(l => l.EntityType).HasColumnName("entity_type").HasMaxLength(50);
        b.Property(l => l.EntityId).HasColumnName("entity_id");
        b.Property(l => l.DetailsJson).HasColumnName("details_json").HasColumnType("jsonb");
        b.Property(l => l.Timestamp).HasColumnName("timestamp");

        b.HasIndex(l => l.Timestamp);
    }
}
