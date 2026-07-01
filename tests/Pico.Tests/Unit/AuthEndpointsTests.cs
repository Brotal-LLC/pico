using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Pico.Api.Endpoints;
using Pico.Domain.Enums;

namespace Pico.Tests.Unit;

public class AuthEndpointsTests
{
    [Fact]
    public void GetCurrentUser_WithFullCookieClaims_ReturnsFullAuthUserDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ctx = CreateHttpContext(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, "admin@pico.local"),
            new Claim(ClaimTypes.Name, "Pico Admin"),
            new Claim(ClaimTypes.Role, UserRole.Admin.ToString()));

        // Act
        var user = AuthEndpoints.GetCurrentUser(ctx);

        // Assert
        Assert.NotNull(user);
        Assert.Equal(userId, user.Id);
        Assert.Equal("admin@pico.local", user.Email);
        Assert.Equal("Pico Admin", user.Name);
        Assert.Equal(UserRole.Admin, user.Role);
    }

    [Theory]
    [InlineData(null, "user@example.com", "Pico User", "Customer")]
    [InlineData("not-a-guid", "user@example.com", "Pico User", "Customer")]
    [InlineData("00000000-0000-0000-0000-000000000001", null, "Pico User", "Customer")]
    [InlineData("00000000-0000-0000-0000-000000000001", "user@example.com", null, "Customer")]
    [InlineData("00000000-0000-0000-0000-000000000001", "user@example.com", "Pico User", null)]
    [InlineData("00000000-0000-0000-0000-000000000001", "user@example.com", "Pico User", "NotARole")]
    public void GetCurrentUser_WithMissingOrMalformedClaims_ReturnsNull(
        string? id,
        string? email,
        string? name,
        string? role)
    {
        // Arrange
        var ctx = CreateHttpContext(
            CreateOptionalClaim(ClaimTypes.NameIdentifier, id),
            CreateOptionalClaim(ClaimTypes.Email, email),
            CreateOptionalClaim(ClaimTypes.Name, name),
            CreateOptionalClaim(ClaimTypes.Role, role));

        // Act
        var user = AuthEndpoints.GetCurrentUser(ctx);

        // Assert
        Assert.Null(user);
    }

    private static DefaultHttpContext CreateHttpContext(params Claim?[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.OfType<Claim>(),
            CookieAuthenticationDefaults.AuthenticationScheme);

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }

    private static Claim? CreateOptionalClaim(string type, string? value) =>
        value is null ? null : new Claim(type, value);
}
