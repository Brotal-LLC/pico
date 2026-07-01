using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Application.Resources;
using Pico.Domain.Entities;
using Pico.Domain.Enums;

namespace Pico.Api.Endpoints;

public record ProvisionEndpointDto(string Name, Guid FlavorId, Guid ImageId);

public static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resources")
            .RequireAuthorization()
            .RequireAntiforgeryForUnsafeMethods();

        // List user's resources
        group.MapGet("/", async (IResourceRepository repo, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var resources = await repo.ListByUserAsync(userId.Value, ct);
            return Results.Ok(resources.Select(r => new
            {
                id = r.Id, name = r.Name, status = r.Status.ToString(),
                flavorId = r.FlavorId, imageId = r.ImageId,
                ipAddress = r.IpAddress, externalId = r.ExternalId,
                createdAt = r.CreatedAt, updatedAt = r.UpdatedAt
            }));
        });

        // Provision a new resource
        group.MapPost("/", async (
            ProvisionEndpointDto req,
            ResourceService svc,
            IAuditLogRepository auditLogs,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { title = "Validation error", detail = "Name is required." });
            if (req.Name.Trim().Length > 64)
                return Results.BadRequest(new { title = "Validation error", detail = "Name must be 64 characters or fewer." });
            var result = await svc.ProvisionAsync(userId.Value,
                new ProvisionRequestDto(req.Name.Trim(), req.FlavorId, req.ImageId), ct);
            if (!result.IsSuccess)
                return Results.BadRequest(new { title = "Provisioning failed", detail = result.ErrorMessage });
            await auditLogs.AddAsync(
                AuditLog.Create(userId.Value, "resource.provision", "Resource", result.Value!.Id,
                    $"{{\"name\":\"{req.Name}\",\"flavorId\":\"{req.FlavorId}\",\"imageId\":\"{req.ImageId}\"}}"),
                ct);
            return Results.Created($"/api/resources/{result.Value!.Id}", result.Value);
        });

        // Get resource detail (ownership-enforced)
        group.MapGet("/{id:guid}", async (Guid id, ResourceService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var isAdmin = AuthEndpoints.IsAdmin(ctx);
            var detail = await svc.GetResourceDetailAsync(id, userId.Value, isAdmin, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        // Start
        group.MapPost("/{id:guid}/start", async (
            Guid id,
            ResourceService svc,
            IAuditLogRepository auditLogs,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var result = await svc.StartAsync(id, userId.Value, ct);
            if (!result.IsSuccess)
                return result.ErrorMessage == "Forbidden" ? Results.Forbid() : Results.BadRequest(new { detail = result.ErrorMessage });
            await auditLogs.AddAsync(
                AuditLog.Create(userId.Value, "resource.start", "Resource", id, "{}"), ct);
            return Results.Ok(result.Value);
        });

        // Stop
        group.MapPost("/{id:guid}/stop", async (
            Guid id,
            ResourceService svc,
            IAuditLogRepository auditLogs,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var result = await svc.StopAsync(id, userId.Value, ct);
            if (!result.IsSuccess)
                return result.ErrorMessage == "Forbidden" ? Results.Forbid() : Results.BadRequest(new { detail = result.ErrorMessage });
            await auditLogs.AddAsync(
                AuditLog.Create(userId.Value, "resource.stop", "Resource", id, "{}"), ct);
            return Results.Ok(result.Value);
        });

        // Terminate
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ResourceService svc,
            IAuditLogRepository auditLogs,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var result = await svc.TerminateAsync(id, userId.Value, ct);
            if (!result.IsSuccess)
                return result.ErrorMessage == "Forbidden" ? Results.Forbid() : Results.BadRequest(new { detail = result.ErrorMessage });
            await auditLogs.AddAsync(
                AuditLog.Create(userId.Value, "resource.terminate", "Resource", id, "{}"), ct);
            return Results.Ok(result.Value);
        });

        // Usage (ownership-enforced)
        group.MapGet("/{id:guid}/usage", async (Guid id, ResourceService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var isAdmin = AuthEndpoints.IsAdmin(ctx);
            var usage = await svc.GetUsageAsync(id, userId.Value, isAdmin, ct);
            return usage is null ? Results.NotFound() : Results.Ok(usage);
        });

        // SSE events stream (ownership-enforced)
        group.MapGet("/{id:guid}/events", async (Guid id, IResourceRepository repo, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var isAdmin = AuthEndpoints.IsAdmin(ctx);

            var resource = await repo.FindByIdAsync(id, ct);
            if (resource is null) return Results.NotFound();
            if (!isAdmin && resource.UserId != userId.Value) return Results.Forbid();

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            // Send existing events (catch-up)
            var events = await repo.ListEventsAsync(id, ct);
            foreach (var e in events)
            {
                var sseData = JsonSerializer.Serialize(new
                {
                    id = e.Id, type = e.EventType,
                    oldStatus = e.OldStatus.ToString(), newStatus = e.NewStatus.ToString(),
                    message = e.Message, timestamp = e.Timestamp
                });
                await ctx.Response.WriteAsync($"data: {sseData}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }

            // Poll for new events
            var sentIds = new HashSet<Guid>(events.Select(e => e.Id));
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1500, ct);
                var newEvents = await repo.ListEventsAsync(id, ct);
                foreach (var e in newEvents.Where(e => sentIds.Add(e.Id)))
                {
                    var sseData = JsonSerializer.Serialize(new
                    {
                        id = e.Id, type = e.EventType,
                        oldStatus = e.OldStatus.ToString(), newStatus = e.NewStatus.ToString(),
                        message = e.Message, timestamp = e.Timestamp
                    });
                    await ctx.Response.WriteAsync($"data: {sseData}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
                await ctx.Response.WriteAsync(": keep-alive\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }

            return Results.Ok();
        });

        return app;
    }
}