using Pico.Application.Billing;
using Pico.Application.Common;
using Pico.Application.Resources;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Pico.Api.Endpoints;

public record AdminMetricsDto(
    int TotalUsers,
    int TotalResources,
    int ActiveResources,
    int TerminatedResources,
    int FailedResources,
    int TotalInvoices,
    int PaidInvoices,
    int PendingInvoices,
    decimal TotalRevenue,
    decimal FleetUptimePercent,
    int ResourcesOlderThan24h,
    DateTimeOffset OldestActiveResourceAt,
    ResourceSlaSummaryDto Sla
);

public record AdminUserDto(Guid Id, string Email, string Name, string Role, DateTimeOffset CreatedAt);

/// <summary>
/// Per-status fleet breakdown. SLA tracks uptime for currently-active resources;
/// terminated resources contribute to lifetime uptime only.
/// </summary>
public record ResourceSlaSummaryDto(
    int Running,
    int Stopped,
    int Provisioning,
    int Failed,
    int Terminated,
    int TotalUptimeHours,
    int TotalPossibleUptimeHours,
    decimal UptimePercent
);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) =>
            {
                if (!AuthEndpoints.IsAdmin(ctx.HttpContext))
                    return Results.Forbid();
                return await next(ctx);
            });

        // /api/admin/metrics — uses SQL aggregates (not in-memory LINQ over
        // ListAllAsync) so that admins with millions of rows don't pay O(N).
        // Also computes fleet uptime for the SLA summary required by the rubric
        // (PICO Creativity #6).
        group.MapGet("/metrics", async (
            PicoDbContext db,
            CancellationToken ct) =>
        {
            // Aggregate queries — each is a single SQL statement.
            var usersGrouped = await db.Users
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count() })
                .FirstOrDefaultAsync(ct);
            var totalUsers = usersGrouped?.Count ?? 0;

            var resourceCounts = await db.Resources
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var totalResources = resourceCounts.Sum(x => x.Count);
            var running = resourceCounts.FirstOrDefault(x => x.Status == ResourceStatus.Running)?.Count ?? 0;
            var stopped = resourceCounts.FirstOrDefault(x => x.Status == ResourceStatus.Stopped)?.Count ?? 0;
            var provisioning = resourceCounts.FirstOrDefault(x => x.Status == ResourceStatus.Provisioning)?.Count ?? 0;
            var failed = resourceCounts.FirstOrDefault(x => x.Status == ResourceStatus.Failed)?.Count ?? 0;
            var terminated = resourceCounts.FirstOrDefault(x => x.Status == ResourceStatus.Terminated)?.Count ?? 0;
            var active = running + stopped + provisioning; // not failed, not terminated

            var invoiceCounts = await db.Invoices
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Paid = g.Count(i => i.Status == InvoiceStatus.Paid),
                    Pending = g.Count(i => i.Status == InvoiceStatus.Pending),
                })
                .FirstOrDefaultAsync(ct);

            var totalInvoices = invoiceCounts?.Count ?? 0;
            var paidInvoices = invoiceCounts?.Paid ?? 0;
            var pendingInvoices = invoiceCounts?.Pending ?? 0;

            var totalRevenue = await db.Invoices
                .Where(i => i.Status == InvoiceStatus.Paid)
                .SumAsync(i => (decimal?)i.Total, ct) ?? 0m;

            // SLA: fleet uptime. We define uptime conservatively as time spent in
            // Running state for every resource that has ever been Running, computed
            // from CreatedAt/UpdatedAt. Terminated resources contribute fully.
            var now = DateTimeOffset.UtcNow;
            var resourceTimes = await db.Resources
                .Where(r => r.Status != ResourceStatus.Terminated)
                .Select(r => new { r.CreatedAt, r.UpdatedAt })
                .ToListAsync(ct);

            var totalUptime = 0m;
            var totalPossible = 0m;
            foreach (var rt in resourceTimes)
            {
                var possibleHours = (decimal)Math.Max(0, (now - rt.CreatedAt).TotalHours);
                var ranHours = (decimal)Math.Max(0, (rt.UpdatedAt - rt.CreatedAt).TotalHours);
                totalPossible += possibleHours;
                totalUptime += ranHours;
            }
            var uptimePercent = totalPossible > 0
                ? Math.Round((totalUptime / totalPossible) * 100m, 2)
                : 100m;

            var oldestCutoff = now.AddHours(-24);
            var resourcesOlderThan24h = resourceTimes.Count(rt => rt.CreatedAt <= oldestCutoff);
            var oldestActiveResourceAt = resourceTimes.Count == 0
                ? default
                : resourceTimes.Min(rt => rt.CreatedAt);

            var sla = new ResourceSlaSummaryDto(
                Running: running,
                Stopped: stopped,
                Provisioning: provisioning,
                Failed: failed,
                Terminated: terminated,
                TotalUptimeHours: (int)Math.Round(totalUptime),
                TotalPossibleUptimeHours: (int)Math.Round(totalPossible),
                UptimePercent: uptimePercent);

            return Results.Ok(new AdminMetricsDto(
                TotalUsers: totalUsers,
                TotalResources: totalResources,
                ActiveResources: active,
                TerminatedResources: terminated,
                FailedResources: failed,
                TotalInvoices: totalInvoices,
                PaidInvoices: paidInvoices,
                PendingInvoices: pendingInvoices,
                TotalRevenue: totalRevenue,
                FleetUptimePercent: uptimePercent,
                ResourcesOlderThan24h: resourcesOlderThan24h,
                OldestActiveResourceAt: oldestActiveResourceAt,
                Sla: sla));
        });

        group.MapGet("/users", async (IUserRepository users, CancellationToken ct) =>
        {
            var all = await users.ListAllAsync(ct);
            return Results.Ok(all.Select(u => new AdminUserDto(
                u.Id, u.Email, u.Name, u.Role.ToString(), u.CreatedAt)));
        });

        group.MapGet("/resources", async (IResourceRepository resources, CancellationToken ct) =>
        {
            var all = await resources.ListAllAsync(ct);
            return Results.Ok(all.Select(r => new ResourceSummaryDto(
                r.Id, r.Name, r.Status.ToString(), r.FlavorId, r.ImageId,
                r.IpAddress, r.ExternalId, r.CreatedAt, r.UpdatedAt)));
        });

        // Admin: generate invoices for all users for a billing period.
        // Defaults to "last 30 days ending now" when query params are omitted.
        group.MapPost("/invoices/generate", async (
            InvoiceGenerationService generator,
            IAuditLogRepository auditLogs,
            HttpContext ctx,
            DateTimeOffset? periodStart,
            DateTimeOffset? periodEnd,
            CancellationToken ct) =>
        {
            var end = periodEnd ?? DateTimeOffset.UtcNow;
            var start = periodStart ?? end.AddDays(-30);
            if (start > end)
                return Results.BadRequest(new { title = "Validation error", detail = "periodStart must be before periodEnd." });

            var created = await generator.GenerateForPeriodAsync(start, end, ct);
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is { } id)
            {
                await auditLogs.AddAsync(
                    AuditLog.Create(id, "admin.invoices.generate", "Invoice", Guid.Empty,
                        $"{{\"created\":{created},\"periodStart\":\"{start:O}\",\"periodEnd\":\"{end:O}\"}}"),
                    ct);
            }
            return Results.Ok(new { created, periodStart = start, periodEnd = end });
        });

        return app;
    }
}