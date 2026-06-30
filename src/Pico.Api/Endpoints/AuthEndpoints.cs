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

public record AuthUserDto(Guid Id, string Email, string Name, string Role);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/signup", async (SignupRequestDto req, IUserRepository users) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "email and password required" });

            var email = req.Email.Trim().ToLowerInvariant();
            if (await users.ExistsByEmailAsync(email, CancellationToken.None))
                return Results.Conflict(new { error = "email already registered" });

            // Demo password hashing — real implementation would use Argon2id
            // stored format: argon2id$<algorithm>$<hash>
            var hash = $"argon2id$dev${Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(req.Password)))}";

            var user = User.Create(email, req.Name, hash, UserRole.Customer);
            await users.AddAsync(user, CancellationToken.None);
            return Results.Ok(ToDto(user));
        });

        group.MapPost("/login", async (LoginRequestDto req, IUserRepository users, HttpContext ctx) =>
        {
            var email = req.Email?.Trim().ToLowerInvariant() ?? "";
            var user = await users.FindByEmailAsync(email, CancellationToken.None);
            if (user is null)
                return Results.Unauthorized();

            // Demo auth — verify by hash prefix match (NOT production-grade)
            var providedHash = $"argon2id$dev${Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(req.Password)))}";
            if (user.PasswordHash != providedHash)
                return Results.Unauthorized();

            await SignInAsync(ctx, user);
            return Results.Ok(ToDto(user));
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/me", (HttpContext ctx) =>
        {
            var user = GetCurrentUser(ctx);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(ToDto(user));
        });

        return app;
    }

    public static async Task SignInAsync(HttpContext ctx, User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }

    public static User? GetCurrentUser(HttpContext ctx)
    {
        var idStr = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idStr is null || !Guid.TryParse(idStr, out var id)) return null;
        // Synchronously resolve via scope
        var scope = ctx.RequestServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        return users.FindByIdAsync(id, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static Guid? GetCurrentUserId(HttpContext ctx)
    {
        var idStr = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idStr, out var id) ? id : null;
    }

    public static bool IsAdmin(HttpContext ctx) =>
        ctx.User.IsInRole(UserRole.Admin.ToString());

    private static AuthUserDto ToDto(User u) =>
        new(u.Id, u.Email, u.Name, u.Role.ToString());
}
