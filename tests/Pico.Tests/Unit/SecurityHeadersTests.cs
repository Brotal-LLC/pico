using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// Regression tests for the security response headers middleware (Gap #2 from AUDIT_REPORT.md).
/// Every response from the API must carry the six security headers — verified live.
/// </summary>
public class SecurityHeadersTests : IClassFixture<CsrfEndpointTests.CsrfWebApplicationFactory>
{
    private readonly CsrfEndpointTests.CsrfWebApplicationFactory _factory;

    public SecurityHeadersTests(CsrfEndpointTests.CsrfWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/catalog/flavors")]
    public async Task AnonymousPublicEndpoint_ReturnsSecurityHeaders(string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Required headers
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var xcto));
        Assert.Equal("nosniff", string.Join(",", xcto));

        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out var xfo));
        Assert.Equal("DENY", string.Join(",", xfo));

        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var rp));
        Assert.Equal("strict-origin-when-cross-origin", string.Join(",", rp));

        Assert.True(response.Headers.TryGetValues("Permissions-Policy", out var pp));
        var permissions = string.Join(",", pp);
        Assert.Contains("camera=()", permissions);
        Assert.Contains("microphone=()", permissions);
        Assert.Contains("geolocation=()", permissions);

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var csp));
        var cspValue = string.Join(",", csp);
        Assert.Contains("default-src 'self'", cspValue);
    }
}