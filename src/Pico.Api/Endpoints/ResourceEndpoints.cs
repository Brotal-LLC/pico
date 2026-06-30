using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Application.Resources;
using Pico.Domain.Entities;

namespace Pico.Api.Endpoints;

public record StartRequestDto(string? Reason);
public record StopRequestDto(string? Reason);
public record ProvisionEndpointRequest(string Name, Guid FlavorId, Guid ImageId);

public static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resources")
            .RequireAuthorization();  // All resource endpoints require auth

        // List user's own resources
        group.MapGet("", async (ResourceService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var list = await svc.ListUserResourcesAsync(userId.Value, ct);
            return Results.Ok(list);
        });

        // Provision a new resource
        group.MapPost("", async (ProvisionEndpointRequest req, ResourceService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var result = await svc.ProvisionAsync(userId.Value,
                new ProvisionRequestDto(req.Name, req.FlavorId, req.ImageId), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.ErrorMessage });
        });

        // Get resource detail (with events)
        group.MapGet("/{id:guid}", async (Guid id, ResourceService svc, CancellationToken ct) =>
        {
            var resource = await svc.GetResourceDetailAsync(id, ct);
            return resource is null ? Results.NotFound() : Results.Ok(resource);
        });

        // Start
        group.MapPost("/{id:guid}/start", async (Guid id, StartRequestDto req, ResourceService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var result = await svc.StartAsync(id, userId.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.ErrorMessage });
        });

        // Stop
        group.MapPost("/{id:guid}/stop", async (Guid id, StopRequestDto req, ResourceService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var result = await svc.StopAsync(id, userId.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.ErrorMessage });
        });

        // Terminate
        group.MapDelete("/{id:guid}", async (Guid id, ResourceService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) return Results.Unauthorized();
            var result = await svc.TerminateAsync(id, userId.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.ErrorMessage });
        });

        // Usage
        group.MapGet("/{id:guid}/usage", async (Guid id, ResourceService svc, CancellationToken ct) =>
        {
            var usage = await svc.GetUsageAsync(id, ct);
            return Results.Ok(usage);
        });

        // SSE events stream — sends events as they occur
        group.MapGet("/{id:guid}/events", async (Guid id, HttpContext ctx, Application.Common.IResourceRepository repo, CancellationToken ct) =>
        {
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null) { ctx.Response.StatusCode = 401; return; }

            // Check resource exists and belongs to user (admin sees all)
            var resource = await repo.FindByIdAsync(id, ct);
            if (resource is null) { ctx.Response.StatusCode = 404; return; }
            if (resource.UserId != userId && !AuthEndpoints.IsAdmin(ctx))
            { ctx.Response.StatusCode = 403; return; }

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            // Send existing events first
            var existing = await repo.ListEventsAsync(id, ct);
            foreach (var evt in existing)
            {
                var json = JsonSerializer.Serialize(new
                {
                    id = evt.Id,
                    type = evt.EventType,
                    oldStatus = evt.OldStatus.ToString(),
                    newStatus = evt.NewStatus.ToString(),
                    message = evt.Message,
                    timestamp = evt.Timestamp
                });
                await ctx.Response.WriteAsync($"data: {json}\n\n");
            }

            // Poll for new events (until client disconnects)
            var lastEventId = existing.LastOrDefault()?.Timestamp ?? DateTimeOffset.UtcNow.AddMinutes(-1);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1500, ct);
                var all = await repo.ListEventsAsync(id, ct);
                var fresh = all.Where(e => e.Timestamp > lastEventId).ToList();
                if (fresh.Any())
                {
                    foreach (var evt in fresh)
                    {
                        var json = JsonSerializer.Serialize(new
                        {
                            id = evt.Id,
                            type = evt.EventType,
                            oldStatus = evt.OldStatus.ToString(),
                            newStatus = evt.NewStatus.ToString(),
                            message = evt.Message,
                            timestamp = evt.Timestamp
                        });
                        await ctx.Response.WriteAsync($"data: {json}\n\n");
                    }
                    lastEventId = fresh.Last().Timestamp;
                }
                else
                {
                    // Keep-alive comment
                    await ctx.Response.WriteAsync(": keep-alive\n\n");
                }
                await ctx.Response.Body.FlushAsync(ct);
            }
        });

        return app;
    }
}
