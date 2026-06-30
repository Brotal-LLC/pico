using Pico.Application.Common;
using Pico.Domain.Entities;
using Pico.Domain.Enums;

namespace Pico.Api.Endpoints;

public record InvoiceListDto(
    Guid Id,
    Guid UserId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal Total,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    int LineCount
);

public record InvoiceLineDto(
    Guid Id,
    Guid ResourceId,
    Guid FlavorId,
    decimal Hours,
    decimal Rate,
    decimal Amount,
    string Description
);

public record InvoiceDetailDto(
    Guid Id,
    Guid UserId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal Total,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    IReadOnlyList<InvoiceLineDto> Lines
);

public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices").RequireAuthorization();

        // List user's own invoices
        group.MapGet("", async (IInvoiceRepository repo, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var isAdmin = AuthEndpoints.IsAdmin(ctx);
            var invoices = isAdmin
                ? await repo.ListAllAsync(ct)
                : await repo.ListByUserAsync(userId.Value, ct);
            return Results.Ok(invoices.Select(ToListDto));
        });

        // Get invoice detail
        group.MapGet("/{id:guid}", async (Guid id, IInvoiceRepository repo, HttpContext ctx, CancellationToken ct) =>
        {
            var invoice = await repo.FindByIdAsync(id, ct);
            if (invoice is null) return Results.NotFound();

            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (invoice.UserId != userId && !AuthEndpoints.IsAdmin(ctx))
                return Results.Forbid();

            return Results.Ok(ToDetailDto(invoice));
        });

        // Mark invoice as paid (simulated payment)
        group.MapPost("/{id:guid}/pay", async (Guid id, IInvoiceRepository repo, HttpContext ctx, CancellationToken ct) =>
        {
            var invoice = await repo.FindByIdAsync(id, ct);
            if (invoice is null) return Results.NotFound();

            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (invoice.UserId != userId) return Results.Forbid();

            if (invoice.IsPaid())
                return Results.Conflict(new { error = "Invoice is already paid" });

            invoice.MarkPaid(DateTimeOffset.UtcNow);
            await repo.UpdateAsync(invoice, ct);
            return Results.Ok(new { ok = true, status = "Paid" });
        });

        return app;
    }

    private static InvoiceListDto ToListDto(Invoice i) =>
        new(i.Id, i.UserId, i.PeriodStart, i.PeriodEnd, i.Total,
            i.Status.ToString(), i.CreatedAt, i.PaidAt, i.Lines.Count);

    private static InvoiceDetailDto ToDetailDto(Invoice i) =>
        new(i.Id, i.UserId, i.PeriodStart, i.PeriodEnd, i.Total,
            i.Status.ToString(), i.CreatedAt, i.PaidAt,
            i.Lines.Select(l => new InvoiceLineDto(
                l.Id, l.ResourceId, l.FlavorId, l.Hours, l.Rate, l.Amount, l.Description)).ToList());
}
