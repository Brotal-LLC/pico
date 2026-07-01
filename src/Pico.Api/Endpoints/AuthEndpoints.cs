using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pico.Application.Common;
using Pico.Domain;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using System.Security.Claims;

namespace Pico.Api.Endpoints;

public record SignupRequestDto(string Email, string Name, string Password);
public record LoginRequestDto(string Email, string Password);
public record AuthUserDto(Guid Id, string Email, string Name, UserRole Role);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .RequireAntiforgeryForUnsafeMethods();

        group.MapPost("/signup", async (
            SignupRequestDto req,
            IUserRepository userRepo,
            IPasswordHasher hasher,
            IAuditLogRepository auditLogs,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { title = "Validation error", detail = "Email and password are required." });

            if (req.Email.Length > 256 || !req.Email.Contains('@'))
                return Results.BadRequest(new { title = "Validation error", detail = "Email is not valid." });

            if (req.Password.Length < 8)
                return Results.BadRequest(new { title = "Validation error", detail = "Password must be at least 8 characters." });

            var name = req.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { title = "Validation error", detail = "Name is required." });

            if (name.Length > 100)
                return Results.BadRequest(new { title = "Validation error", detail = "Name must be 100 characters or fewer." });

            if (await userRepo.ExistsByEmailAsync(req.Email.Trim(), ct))
                return Results.Conflict(new { title = "Conflict", detail = "An account with this email already exists." });

            var hash = hasher.Hash(req.Password);
            var user = User.Create(req.Email.Trim(), name, hash, UserRole.Customer);
            await userRepo.AddAsync(user, ct);

            await auditLogs.AddAsync(
                AuditLog.Create(user.Id, "signup", "User", user.Id, $"{{\"email\":\"{user.Email}\"}}"),
                ct);

            await SignInAsync(ctx, user);
            return Results.Ok(ToDto(user));
        }).RequireRateLimiting("auth-ip");

        group.MapPost("/login", async (
            LoginRequestDto req,
            IUserRepository userRepo,
            IPasswordHasher hasher,
            IAuditLogRepository auditLogs,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { title = "Validation error", detail = "Email and password are required." });

            var user = await userRepo.FindByEmailAsync(req.Email.Trim(), ct);
            var ok = user is not null && hasher.Verify(req.Password, user.PasswordHash);

            // Always log the attempt — never include the password.
            await auditLogs.AddAsync(
                AuditLog.Create(
                    user?.Id,
                    "login",
                    "User",
                    user?.Id ?? Guid.Empty,
                    $"{{\"success\":{(ok ? "true" : "false")}}}"),
                ct);

            if (!ok) return Results.Unauthorized();

            await SignInAsync(ctx, user!);
            return Results.Ok(ToDto(user!));
        }).RequireRateLimiting("auth-ip");

        group.MapPost("/logout", async (
            HttpContext ctx,
            IAuditLogRepository auditLogs,
            CancellationToken ct) =>
        {
            var userId = GetCurrentUserId(ctx);
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (userId is { } id)
            {
                await auditLogs.AddAsync(
                    AuditLog.Create(id, "logout", "User", id, "{}"),
                    ct);
            }
            return Results.Ok(new { message = "Logged out" });
        });

        group.MapGet("/me", (HttpContext ctx) =>
        {
            var user = GetCurrentUser(ctx);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(user);
        }).RequireAuthorization();

        return app;
    }

    private static async Task SignInAsync(HttpContext ctx, User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        // Issue a persistent (7-day) cookie. Without this the auth cookie is
        // session-only despite SlidingExpiration=7d. See AUDIT_REPORT §S4.
        var props = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
            AllowRefresh = true
        };
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
    }

    private static AuthUserDto ToDto(User user) =>
        new(user.Id, user.Email, user.Name, user.Role);

    public static AuthUserDto? GetCurrentUser(HttpContext ctx)
    {
        var id = GetCurrentUserId(ctx);
        var email = ctx.User.FindFirstValue(ClaimTypes.Email);
        var name = ctx.User.FindFirstValue(ClaimTypes.Name);
        var roleStr = ctx.User.FindFirstValue(ClaimTypes.Role);

        if (id is null || email is null || name is null || roleStr is null)
            return null;

        if (!Enum.TryParse<UserRole>(roleStr, out var role))
            return null;

        return new AuthUserDto(id.Value, email, name, role);
    }

    public static Guid? GetCurrentUserId(HttpContext ctx)
    {
        var idStr = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idStr, out var id) ? id : null;
    }

    public static bool IsAdmin(HttpContext ctx) =>
        ctx.User.IsInRole(UserRole.Admin.ToString());
}