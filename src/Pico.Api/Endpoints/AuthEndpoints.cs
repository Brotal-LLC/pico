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
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { title = "Validation error", detail = "Email and password are required." });

            if (req.Password.Length < 6)
                return Results.BadRequest(new { title = "Validation error", detail = "Password must be at least 6 characters." });

            if (await userRepo.ExistsByEmailAsync(req.Email, ct))
                return Results.Conflict(new { title = "Conflict", detail = "An account with this email already exists." });

            var hash = hasher.Hash(req.Password);
            var user = User.Create(req.Email.Trim(), req.Name.Trim(), hash, UserRole.Customer);
            await userRepo.AddAsync(user, ct);

            await SignInAsync(ctx, user);
            return Results.Ok(ToDto(user));
        });

        group.MapPost("/login", async (
            LoginRequestDto req,
            IUserRepository userRepo,
            IPasswordHasher hasher,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { title = "Validation error", detail = "Email and password are required." });

            var user = await userRepo.FindByEmailAsync(req.Email.Trim(), ct);
            if (user is null || !hasher.Verify(req.Password, user.PasswordHash))
                return Results.Unauthorized();

            await SignInAsync(ctx, user);
            return Results.Ok(ToDto(user));
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
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