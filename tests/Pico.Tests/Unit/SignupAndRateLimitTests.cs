using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// Regression test for AUDIT_REPORT §S1: signup with omitted Name must return 400,
/// not 500 (NullReferenceException on req.Name.Trim()).
/// </summary>
public class SignupValidationTests : IClassFixture<CsrfEndpointTests.CsrfWebApplicationFactory>
{
    private readonly CsrfEndpointTests.CsrfWebApplicationFactory _factory;

    public SignupValidationTests(CsrfEndpointTests.CsrfWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("", "user@example.com", "validPass123")]
    [InlineData(null, "user@example.com", "validPass123")]
    [InlineData("   ", "user@example.com", "validPass123")]
    public async Task Signup_MissingOrBlankName_Returns400(string? name, string email, string password)
    {
        using var client = _factory.CreateClient();
        var tokenResponse = await client.GetFromJsonAsync<CsrfEndpointTests.CsrfTokenResponse>("/api/auth/csrf-token");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signup")
        {
            Content = JsonContent.Create(new { name, email, password })
        };
        request.Headers.Add("X-CSRF-TOKEN", tokenResponse!.Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signup_WeakPassword_Returns400()
    {
        using var client = _factory.CreateClient();
        var tokenResponse = await client.GetFromJsonAsync<CsrfEndpointTests.CsrfTokenResponse>("/api/auth/csrf-token");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signup")
        {
            Content = JsonContent.Create(new { name = "Test User", email = "u@example.com", password = "short" })
        };
        request.Headers.Add("X-CSRF-TOKEN", tokenResponse!.Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signup_InvalidEmail_Returns400()
    {
        using var client = _factory.CreateClient();
        var tokenResponse = await client.GetFromJsonAsync<CsrfEndpointTests.CsrfTokenResponse>("/api/auth/csrf-token");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signup")
        {
            Content = JsonContent.Create(new { name = "Test User", email = "not-an-email", password = "validPass123" })
        };
        request.Headers.Add("X-CSRF-TOKEN", tokenResponse!.Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

/// <summary>
/// Regression test for AUDIT_REPORT §S2: rate limiting on login/signup.
/// 20 attempts / 15 min / IP — the 21st must return 429.
/// </summary>
public class RateLimitTests : IClassFixture<CsrfEndpointTests.CsrfWebApplicationFactory>
{
    private readonly CsrfEndpointTests.CsrfWebApplicationFactory _factory;

    public RateLimitTests(CsrfEndpointTests.CsrfWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Signup_TwentyFirstAttempt_Returns429()
    {
        using var client = _factory.CreateClient();

        // First 20 attempts should pass through (return 400 due to bad payload or 401/etc — not 429)
        for (int i = 0; i < 20; i++)
        {
            var tokenResponse = await client.GetFromJsonAsync<CsrfEndpointTests.CsrfTokenResponse>("/api/auth/csrf-token");
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signup")
            {
                Content = JsonContent.Create(new { name = "RL", email = "x@example.com", password = "short" })
            };
            req.Headers.Add("X-CSRF-TOKEN", tokenResponse!.Token);
            var r = await client.SendAsync(req);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode);
        }

        // 21st attempt must be rate-limited
        var token = await client.GetFromJsonAsync<CsrfEndpointTests.CsrfTokenResponse>("/api/auth/csrf-token");
        var req6 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signup")
        {
            Content = JsonContent.Create(new { name = "RL", email = "x@example.com", password = "validPass123" })
        };
        req6.Headers.Add("X-CSRF-TOKEN", token!.Token);
        var r6 = await client.SendAsync(req6);

        Assert.Equal(HttpStatusCode.TooManyRequests, r6.StatusCode);
    }
}