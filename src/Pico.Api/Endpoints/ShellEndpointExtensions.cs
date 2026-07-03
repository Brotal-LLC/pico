using System.Net.WebSockets;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Application.Resources;
using Pico.Domain.Enums;

namespace Pico.Api.Endpoints;

/// <summary>
/// WebSocket endpoint for interactive VM shell access.
/// Route: GET /api/resources/{id:guid}/shell (upgrades to WebSocket)
///
/// Security:
///   - Cookie auth (same as all resource endpoints)
///   - Origin header checked against CORS allowlist to prevent CSWSH
///   - Resource ownership enforced (admin bypass)
///   - Session killed on WebSocket close or client disconnect
/// </summary>
public static class ShellEndpointExtensions
{
    public static IEndpointRouteBuilder MapShellEndpoint(this IEndpointRouteBuilder app, string[] allowedOrigins)
    {
        app.MapGet("/api/resources/{id:guid}/shell", async (
            Guid id,
            HttpContext ctx,
            IResourceRepository repo,
            IProvisioningBackend backend,
            CancellationToken ct) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
                return Results.BadRequest(new { title = "WebSocket required", detail = "This endpoint requires a WebSocket connection." });

            // ── Auth ────────────────────────────────────────────────────────
            var userId = AuthEndpoints.GetCurrentUserId(ctx);
            if (userId is null)
                return Results.Unauthorized();

            // ── Origin check (CSWSH protection) ────────────────────────────
            var origin = ctx.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin))
            {
                var normalized = origin.TrimEnd('/');
                if (!allowedOrigins.Any(o => o.TrimEnd('/').Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                    return Results.Forbid();
            }

            // ── Ownership ──────────────────────────────────────────────────
            var resource = await repo.FindByIdAsync(id, ct);
            if (resource is null)
                return Results.NotFound();

            var isAdmin = AuthEndpoints.IsAdmin(ctx);
            if (!isAdmin && resource.UserId != userId.Value)
                return Results.Forbid();

            if (string.IsNullOrEmpty(resource.ExternalId))
                return Results.BadRequest(new { title = "No external ID", detail = "This resource has no backing container." });

            if (resource.Status != ResourceStatus.Running)
                return Results.BadRequest(new { title = "VM not running", detail = "The VM must be in Running state to open a shell." });

            // ── Accept and bridge ──────────────────────────────────────────
            var ws = await ctx.WebSockets.AcceptWebSocketAsync();

            await using var session = await backend.ExecInteractiveAsync(resource.ExternalId, ct);

            // Two-directional pump:
            //   WS → session.InputStream  (user keystrokes)
            //   session.OutputStream → WS (shell output)
            //
            // We run the output pump on the calling thread and the input
            // pump on a background Task. When either side closes, we kill
            // the session which unblocks the output pump's ReadAsync.

            var inputTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (true)
                    {
                        var result = await ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        if (result.Count > 0)
                            await session.InputStream.WriteAsync(buffer.AsMemory(0, result.Count), ct);
                    }
                }
                catch (WebSocketException) { }
                catch (OperationCanceledException) { }
                finally
                {
                    session.Kill();
                }
            }, ct);

            var outputBuffer = new byte[8192];
            try
            {
                while (true)
                {
                    var read = await session.OutputStream.ReadAsync(outputBuffer, ct);
                    if (read == 0)
                        break;

                    await ws.SendAsync(
                        outputBuffer.AsMemory(0, read),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct);
                }
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }

            // Clean up: kill session, close WS
            session.Kill();
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
            }
            catch { }

            try { await inputTask; } catch { }

            return Results.Ok();
        }).RequireAuthorization();

        return app;
    }
}