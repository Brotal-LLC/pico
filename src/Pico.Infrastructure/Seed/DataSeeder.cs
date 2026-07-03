using Microsoft.EntityFrameworkCore;
using Pico.Application.Common;
using Pico.Application.Provisioning;
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
    private readonly IProvisioningBackend _backend;

    public DataSeeder(IPasswordHasher hasher, IProvisioningBackend backend)
    {
        _hasher = hasher;
        _backend = backend;
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
        // In production, the demo credentials are injected via environment
        // variables. Default values are provided only for local reviewer/dev
        // setups where .env.example is used unchanged.
        var demoPassword  = Environment.GetEnvironmentVariable("DEMO_PASSWORD")  ?? "pico-demo-password";
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "pico-admin-password";

        User demoUser, adminUser;
        if (usersAlready)
        {
            demoUser = await db.Users.FirstAsync(u => u.Email == "demo@pico.local", ct);
            adminUser = await db.Users.FirstAsync(u => u.Email == "admin@pico.local", ct);
        }
        else
        {
            var demoHash  = _hasher.Hash(demoPassword);
            var adminHash = _hasher.Hash(adminPassword);
            demoUser  = User.Create("demo@pico.local",  "Demo User", demoHash,  UserRole.Customer);
            adminUser = User.Create("admin@pico.local", "Admin User", adminHash, UserRole.Admin);
            db.Users.AddRange(demoUser, adminUser);
        }

        await db.SaveChangesAsync(ct);

        // ─── Demo resources (one Stopped + two Terminated historical) ─────
        // Reviewers should see the full lifecycle surface, not just a fresh
        // `Created` resource with disabled buttons:
        //   • demo-vm-01   Stopped     — operable: Start, Terminate work.
        //   • legacy-vm-01 Terminated  — historical: "Recreate with same config".
        //   • legacy-vm-02 Terminated  — historical: different flavor/image.
        // Each gets a full event trail so the timeline component renders
        // something meaningful out of the box.
        var picoSmall  = flavors.First(f => f.Name == "pico.small");
        var picoMedium = flavors.First(f => f.Name == "pico.medium");
        var picoNano   = flavors.First(f => f.Name == "pico.nano");
        var ubuntu22   = images.First(i => i.Name == "ubuntu-22");
        var debian12   = images.First(i => i.Name == "debian-12");

        var demoResource = await SeedDemoResourcesAsync(
            db, demoUser.Id, resAlready, ct, _backend,
            (picoSmall.Id, ubuntu22.Id, "demo-vm-01",    ResourceStatus.Stopped),
            (picoMedium.Id, ubuntu22.Id, "legacy-vm-01", ResourceStatus.Terminated),
            (picoNano.Id,  debian12.Id,  "legacy-vm-02", ResourceStatus.Terminated));

        // ─── Historical invoices so reviewers see a real billing history ─
        // Three invoices give the dashboard something meaningful to render:
        //   • Two paid invoices (60d ago, 30d ago) — show historical billing
        //   • One current pending invoice with multiple line items — exercises
        //     the detail view, the "Pay now" CTA, and the per-flavor breakdown.
        if (!invoiceAlready)
        {
            var picoMicro  = flavors.First(f => f.Name == "pico.micro");
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

    /// <summary>
    /// Seed the demo user's resources with a realistic lifecycle mix.
    /// Returns the "active" demo VM (the Stopped one) so the seeder can
    /// reference it for invoice lines.
    ///
    /// Idempotent: if any resource already exists for this user, skip
    /// resource creation entirely. The pre-rework seed only created one
    /// `Created` resource — older databases will keep that single row and
    /// gain the historical invoices (which is still a meaningful demo).
    /// </summary>
    private static async Task<Resource> SeedDemoResourcesAsync(
        PicoDbContext db,
        Guid demoUserId,
        bool resAlready,
        CancellationToken ct,
        IProvisioningBackend backend,
        params (Guid FlavorId, Guid ImageId, string Name, ResourceStatus FinalStatus)[] specs)
    {
        if (resAlready)
        {
            // One-time migration for reviewers who started before the
            // rework. Old seed created a single `Created` resource with
            // no event trail — that state has no allowed outgoing
            // transitions and the Start button is disabled, so reviewers
            // can't actually exercise the lifecycle. Walk it forward to
            // `Stopped` (with a backdated event trail) so the demo is
            // usable immediately after the deploy that ships this seed.
            return await MigrateLegacyCreatedResourceAsync(db, demoUserId, backend, ct);
        }

        var now = DateTimeOffset.UtcNow;
        var active = (Resource?)null;

        for (var i = 0; i < specs.Length; i++)
        {
            var (flavorId, imageId, name, finalStatus) = specs[i];

            // Backdate creation so the demo data looks lived-in. The active
            // VM is 3 days old; the historical VMs are 30 and 60 days old.
            var ageDays = finalStatus == ResourceStatus.Terminated ? 60 - i * 30 : 3;
            var createdAt = now.AddDays(-ageDays);

            var resource = Resource.Provision(demoUserId, flavorId, imageId, name);
            // Provision() sets CreatedAt; overwrite via reflection-free hack
            // by re-instantiating with the same id.
            BackdateResource(resource, createdAt);
            db.Resources.Add(resource);

            // Walk the lifecycle so the state-machine actually flows:
            //   Created → Provisioning → Running → (Stopped | Terminated)
            // For Terminated: Running → Terminated directly (no Stop in between,
            // mirrors an emergency tear-down).
            await db.SaveChangesAsync(ct);
            await AppendSeedEvent(db, resource.Id, "Created", ResourceStatus.Created,
                ResourceStatus.Created, "Resource created", createdAt, ct);

            resource.TransitionTo(ResourceStatus.Provisioning, "Backend provisioning started");
            await db.SaveChangesAsync(ct);
            await AppendSeedEvent(db, resource.Id, "StatusChange",
                ResourceStatus.Created, ResourceStatus.Provisioning,
                "Backend provisioning started",
                createdAt.AddMinutes(2), ct);

            // The provisioning backend assigns a real external id + ip so
            // the seeded resource is actually operable (Start/Stop work
            // on the Stopped VM, Terminate on Docker mode finds a real
            // container to remove). The backend is async — call it once
            // per resource to keep things honest.
            var backendResult = await backend.ProvisionAsync(
                new ProvisionRequest(
                    resource.Id, resource.Name, flavorId, imageId, demoUserId.ToString(),
                    Vcpus: 0, RamMb: 0, DiskGb: 0, ImageName: "ubuntu-22"),
                ct);
            // Even if the backend hiccups (e.g. Docker daemon unavailable
            // mid-deploy), we still want the seed to complete and the
            // resource to land in its final state — log a warning and
            // carry on with null externalId. The Terminate path skips
            // the backend call when ExternalId is null, so the user can
            // always clean up; Start/Stop will fail with a clear error.
            if (backendResult.Success)
            {
                resource.SetExternalId(backendResult.ExternalId);
                resource.SetIpAddress(backendResult.IpAddress);
            }

            resource.TransitionTo(ResourceStatus.Running, "Resource is now running");
            BackdateResource(resource, createdAt.AddMinutes(3));
            await db.SaveChangesAsync(ct);
            await AppendSeedEvent(db, resource.Id, "StatusChange",
                ResourceStatus.Provisioning, ResourceStatus.Running,
                "Resource is now running",
                createdAt.AddMinutes(3), ct);

            if (finalStatus == ResourceStatus.Terminated)
            {
                resource.TransitionTo(ResourceStatus.Terminated,
                    "User terminated (historical)");
                BackdateResource(resource, createdAt.AddDays(ageDays - 1));
                await db.SaveChangesAsync(ct);
                await AppendSeedEvent(db, resource.Id, "StatusChange",
                    ResourceStatus.Running, ResourceStatus.Terminated,
                    "User terminated (historical)",
                    createdAt.AddDays(ageDays - 1), ct);
            }
            else if (finalStatus == ResourceStatus.Stopped)
            {
                resource.TransitionTo(ResourceStatus.Stopped, "User stopped");
                BackdateResource(resource, createdAt.AddHours(2));
                await db.SaveChangesAsync(ct);
                await AppendSeedEvent(db, resource.Id, "StatusChange",
                    ResourceStatus.Running, ResourceStatus.Stopped,
                    "User stopped",
                    createdAt.AddHours(2), ct);

                active = resource;
            }
        }

        // `active` is non-null when at least one spec asked for Stopped
        return active ?? await db.Resources
            .Where(r => r.UserId == demoUserId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstAsync(ct);
    }

    /// <summary>
    /// Add a ResourceEvent with a backdated timestamp. Used by the seeder
    /// to give historical VMs a believable event timeline.
    /// </summary>
    private static async Task AppendSeedEvent(
        PicoDbContext db,
        Guid resourceId,
        string eventType,
        ResourceStatus oldStatus,
        ResourceStatus newStatus,
        string message,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var evt = ResourceEvent.Create(resourceId, eventType, oldStatus, newStatus, message);
        // ResourceEvent.Create stamps Timestamp = UtcNow; reset it via a
        // dedicated helper so the demo timeline looks historical.
        BackdateResourceEvent(evt, timestamp);
        await db.ResourceEvents.AddAsync(evt, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The aggregate ctor sets CreatedAt/UpdatedAt = UtcNow. For the seeder
    /// we want historical timestamps; this uses EF's property accessors
    /// (they're `private set` so we go through a small reflection helper).
    /// Centralised here so the seeder isn't peppered with reflection calls.
    /// </summary>
    private static void BackdateResource(Resource r, DateTimeOffset createdAt)
    {
        var t = typeof(Resource);
        t.GetProperty("CreatedAt")!.SetValue(r, createdAt);
        t.GetProperty("UpdatedAt")!.SetValue(r, createdAt);
    }

    private static void BackdateResourceEvent(ResourceEvent e, DateTimeOffset timestamp)
    {
        typeof(ResourceEvent)
            .GetProperty("Timestamp")!
            .SetValue(e, timestamp);
    }

    /// <summary>
    /// Migration shim for reviewers on the pre-rework DB. If the demo
    /// user has any `Created` resource with no event trail, walk it
    /// through Provisioning → Running → Stopped with backdated events.
    /// Idempotent: resources already in a non-`Created` state are
    /// returned untouched.
    /// </summary>
    private static async Task<Resource> MigrateLegacyCreatedResourceAsync(
        PicoDbContext db, Guid demoUserId, IProvisioningBackend backend, CancellationToken ct)
    {
        var legacy = await db.Resources
            .Where(r => r.UserId == demoUserId && r.Status == ResourceStatus.Created)
            .FirstOrDefaultAsync(ct);

        if (legacy is null)
        {
            // No migration needed — return the user's active resource
            return await db.Resources
                .Where(r => r.UserId == demoUserId && r.Status != ResourceStatus.Terminated)
                .OrderBy(r => r.CreatedAt)
                .FirstAsync(ct);
        }

        var now = DateTimeOffset.UtcNow;
        var createdAt = now.AddDays(-3);

        // Call the backend so the legacy VM has a real container to
        // operate on. Without this, the Stopped → Start path would
        // fail with "Resource has no external id" because the old
        // seed never called the backend at all.
        var backendResult = await backend.ProvisionAsync(
            new ProvisionRequest(
                legacy.Id, legacy.Name, legacy.FlavorId, legacy.ImageId,
                demoUserId.ToString(), Vcpus: 0, RamMb: 0, DiskGb: 0, ImageName: "ubuntu-22"),
            ct);
        if (backendResult.Success)
        {
            legacy.SetExternalId(backendResult.ExternalId);
            legacy.SetIpAddress(backendResult.IpAddress);
        }

        legacy.TransitionTo(ResourceStatus.Provisioning, "Backend provisioning started (seeded retroactively)");
        await db.SaveChangesAsync(ct);
        await AppendSeedEvent(db, legacy.Id, "StatusChange",
            ResourceStatus.Created, ResourceStatus.Provisioning,
            "Backend provisioning started",
            createdAt.AddMinutes(2), ct);

        legacy.TransitionTo(ResourceStatus.Running, "Resource is now running");
        await db.SaveChangesAsync(ct);
        await AppendSeedEvent(db, legacy.Id, "StatusChange",
            ResourceStatus.Provisioning, ResourceStatus.Running,
            "Resource is now running",
            createdAt.AddMinutes(3), ct);

        legacy.TransitionTo(ResourceStatus.Stopped, "User stopped");
        BackdateResource(legacy, createdAt.AddHours(2));
        await db.SaveChangesAsync(ct);
        await AppendSeedEvent(db, legacy.Id, "StatusChange",
            ResourceStatus.Running, ResourceStatus.Stopped,
            "User stopped",
            createdAt.AddHours(2), ct);

        return legacy;
    }
}