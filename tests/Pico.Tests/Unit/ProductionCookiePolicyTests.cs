using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Infrastructure;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// Regression tests for the antiforgery / cookie SecurePolicy configuration.
/// The pre-fix code forced <c>CookieSecurePolicy.Always</c> in non-Testing
/// environments, which broke local <c>docker compose up --build</c> reviewers
/// because the production image runs in ASPNETCORE_ENVIRONMENT=Production
/// over plain HTTP. The antiforgery middleware then refused to issue the
/// csrf cookie, and the first POST (e.g. <c>/api/auth/login</c>) crashed with
/// "SecurePolicy = Always, but the current request is not an SSL request" (500).
///
/// These tests boot the full app under <c>UseEnvironment("Production")</c> to
/// reproduce the reviewer's environment, then assert the CSRF cookie is issued
/// and a login POST does not 500.
/// </summary>
public class ProductionCookiePolicyTests : IClassFixture<ProductionCookiePolicyTests.ProductionWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _factory;

    public ProductionCookiePolicyTests(ProductionWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CsrfTokenEndpoint_InProductionEnv_StillIssuesCookie()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var response = await client.GetAsync("/api/auth/csrf-token");

        // The cookie must be set on the response. If SecurePolicy=Always and
        // the request is plain HTTP, the antiforgery middleware throws 500
        // before it can issue the cookie.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies),
            "Expected Set-Cookie header on the csrf-token response");
        Assert.Contains(setCookies!, c => c.Contains("Pico.Antiforgery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoginPost_InProductionEnv_ReachesAuthHandler_NotAntiforgeryMiddleware()
    {
        // The point of this test: the antiforgery middleware must NOT crash
        // with a 500 ("SecurePolicy = Always, but the current request is not
        // an SSL request") when the API is hit over plain HTTP in a Production
        // environment. If the middleware crashed, the response would be 500.
        // With the fix the middleware succeeds and the endpoint returns 401
        // (no matching user in the fake repo).
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var tokenResponse = await client.GetFromJsonAsync<TokenResponse>("/api/auth/csrf-token");
        Assert.NotNull(tokenResponse);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "reviewer@example.com", password = "irrelevant" })
        };
        request.Headers.Add("X-CSRF-TOKEN", tokenResponse!.Token);

        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    public sealed record TokenResponse(string Token);

    /// <summary>
    /// WebApplicationFactory that boots the app with the SAME environment
    /// that <c>Dockerfile.prod</c> bakes in: <c>ASPNETCORE_ENVIRONMENT=Production</c>.
    /// This is the exact setup a reviewer gets from
    /// <c>docker compose up --build</c>, so tests here will catch any
    /// production-only cookie / antiforgery misconfig.
    /// </summary>
    public sealed class ProductionWebApplicationFactory : WebApplicationFactory<Program>
    {
        public ProductionWebApplicationFactory()
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Default",
                "Host=localhost;Database=pico_tests;Username=postgres;Password=postgres");
            Environment.SetEnvironmentVariable("PROVISIONING_MODE", "mock");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");

            builder.ConfigureTestServices(services =>
            {
                // Strip hosted services + real repos; we only need the HTTP
                // pipeline to start so the antiforgery middleware is reachable.
                services.RemoveAll<IHostedService>();
                services.RemoveAll<DatabaseInitializer>();
                services.RemoveAll<IUserRepository>();
                services.RemoveAll<IFlavorRepository>();
                services.RemoveAll<IImageRepository>();
                services.RemoveAll<IResourceRepository>();
                services.RemoveAll<IInvoiceRepository>();
                services.RemoveAll<IAuditLogRepository>();
                services.RemoveAll<IProvisioningBackend>();

                services.AddSingleton<IUserRepository, EmptyUserRepository>();
                services.AddSingleton<IFlavorRepository, EmptyFlavorRepository>();
                services.AddSingleton<IImageRepository, EmptyImageRepository>();
                services.AddSingleton<IResourceRepository, EmptyResourceRepository>();
                services.AddSingleton<IInvoiceRepository, EmptyInvoiceRepository>();
                services.AddSingleton<IAuditLogRepository, EmptyAuditLogRepository>();
                services.AddSingleton<IProvisioningBackend, EmptyProvisioningBackend>();
            });
        }
    }

    private sealed class EmptyUserRepository : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task AddAsync(User user, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyFlavorRepository : IFlavorRepository
    {
        public Task<Flavor?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Flavor?>(null);
        public Task<IReadOnlyList<Flavor>> ListActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Flavor>>(Array.Empty<Flavor>());
        public Task<IReadOnlyList<Flavor>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Flavor>>(Array.Empty<Flavor>());
        public Task AddAsync(Flavor flavor, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyImageRepository : IImageRepository
    {
        public Task<Image?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Image?>(null);
        public Task<IReadOnlyList<Image>> ListActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Image>>(Array.Empty<Image>());
        public Task AddAsync(Image image, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyResourceRepository : IResourceRepository
    {
        public Task<Resource?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Resource?>(null);
        public Task<IReadOnlyList<Resource>> ListByUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Resource>>(Array.Empty<Resource>());
        public Task<IReadOnlyList<Resource>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Resource>>(Array.Empty<Resource>());
        public Task<IReadOnlyList<Resource>> ListActiveByUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Resource>>(Array.Empty<Resource>());
        public Task AddAsync(Resource resource, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(Resource resource, CancellationToken ct) => Task.CompletedTask;
        public Task AddEventAsync(ResourceEvent evt, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ResourceEvent>> ListEventsAsync(Guid resourceId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ResourceEvent>>(Array.Empty<ResourceEvent>());
    }

    private sealed class EmptyInvoiceRepository : IInvoiceRepository
    {
        public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Invoice?>(null);
        public Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Invoice>>(Array.Empty<Invoice>());
        public Task<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Invoice>>(Array.Empty<Invoice>());
        public Task AddAsync(Invoice invoice, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(Invoice invoice, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyAuditLogRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog log, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditLog>> ListAsync(DateTimeOffset since, CancellationToken ct) => Task.FromResult<IReadOnlyList<AuditLog>>(Array.Empty<AuditLog>());
    }

    private sealed class EmptyProvisioningBackend : IProvisioningBackend
    {
        public string Mode => "fake";
        public Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct) => Task.FromResult(ProvisionResult.Ok("external-test", "127.0.0.1"));
        public Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct) => Task.FromResult(ProvisionResult.Ok(externalId, "127.0.0.1"));
        public Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct) => Task.FromResult(ProvisionResult.Ok(externalId, "127.0.0.1"));
        public Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct) => Task.FromResult(ProvisionResult.Ok(externalId, "127.0.0.1"));
        public Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct) => Task.FromResult(ResourceUsage.Empty());
        public Task<BackendHealth> GetHealthAsync(CancellationToken ct) => Task.FromResult(new BackendHealth(Mode, true, null, DateTimeOffset.UtcNow));
        public Task<IShellSession> ExecInteractiveAsync(string externalId, CancellationToken ct) =>
            throw new NotSupportedException("Test stub does not implement interactive exec.");
    }
}
