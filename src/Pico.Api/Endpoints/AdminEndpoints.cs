using Pico.Application.Billing;
using Pico.Application.Common;
using Pico.Application.Resources;
using Pico.Domain.Entities;

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
    decimal TotalRevenue
);

public record AdminUserDto(Guid Id, string Email, string Name, string Role, DateTimeOffset CreatedAt);

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

        group.MapGet("/metrics", async (
            IUserRepository users,
            IResourceRepository resources,
            IInvoiceRepository invoices,
            CancellationToken ct) =>
        {
            var allUsers = await users.ListAllAsync(ct);
            var allResources = await resources.ListAllAsync(ct);
            var allInvoices = await invoices.ListAllAsync(ct);

            var totalRev = allInvoices.Where(i => i.IsPaid()).Sum(i => i.Total);

            return Results.Ok(new AdminMetricsDto(
                TotalUsers: allUsers.Count,
                TotalResources: allResources.Count,
                ActiveResources: allResources.Count(r => !r.IsTerminated()),
                TerminatedResources: allResources.Count(r => r.IsTerminated()),
                FailedResources: allResources.Count(r => r.IsFailed()),
                TotalInvoices: allInvoices.Count,
                PaidInvoices: allInvoices.Count(i => i.IsPaid()),
                PendingInvoices: allInvoices.Count(i => !i.IsPaid()),
                TotalRevenue: totalRev));
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