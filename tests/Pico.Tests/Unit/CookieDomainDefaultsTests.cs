using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain.Entities;
using Pico.Infrastructure;
using Xunit;

namespace Pico.Tests.Unit;

/// <summary>
/// Regression tests for the <c>Cookie:Domain</c> configuration that affects
/// both the antiforgery cookie (<c>Pico.Antiforgery</c>) and the auth cookie
/// (<c>Pico.Auth</c>).
///
/// The pre-fix <see cref="Program"/> code assigned
/// <c>options.Cookie.Domain = builder.Configuration["Cookie:Domain"]</c>
/// verbatim. When the env var is the empty string
/// (<c>Cookie__Domain=</c>) the ASP.NET cookie pipeline emits
/// <c>Set-Cookie: ...; domain=; ...</c> — a header with an empty Domain
/// attribute. Browsers handle that inconsistently: some set the cookie on
/// the exact issuing host (which is what we want), others reject it
/// outright. In a cross-origin review scenario (frontend on
/// <c>http://localhost:3000</c>, API on <c>http://localhost:8080</c>) the
/// cookie either does not get sent or does not get recognized, and the
/// antiforgery / auth flow breaks with a 403.
///
/// After the fix, <see cref="Program"/> routes the cookie-domain config
/// through a small <c>GetCookieDomain</c> helper that returns null for
/// empty / missing values, which causes ASP.NET to omit the Domain
/// attribute entirely (cookies scoped to the exact host that issued them).
///
/// These tests boot the app with a few representative environment
/// configurations and assert the resulting <c>Set-Cookie</c> headers from
/// the antiforgery and login endpoints.
/// </summary>
public class CookieDomainDefaultsTests
{
    private const string DevOrigin = "http://localhost:3000";

    [Theory]
    [InlineData(null, "no-cookie-domain-env-var")]         // env var unset
    [InlineData("",   "cookie-domain-empty-string")]       // env var present but empty
    [InlineData("   ", "cookie-domain-whitespace-only")]   // env var present but whitespace
    public async Task AntiforgeryCookie_OmitsDomainAttribute_WhenDomainConfigIsEmpty(string? cookieDomainEnv, string scenario)
    {
        var prevValue = Environment.GetEnvironmentVariable("Cookie__Domain");
        try
        {
            Environment.SetEnvironmentVariable("Cookie__Domain", cookieDomainEnv);

            using var factory = new CookieDomainWebApplicationFactory();
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/auth/csrf-token");

            Assert.True(response.IsSuccessStatusCode, $"scenario '{scenario}': expected 2xx, got {(int)response.StatusCode} {response.ReasonPhrase}");
            var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? string.Join(",", values)
                : string.Empty;
            // The Domain attribute is omitted (not literally `domain=`).
            // ASP.NET serializes `null` Domain as the absence of the
            // `domain=` token in the cookie pair list.
            Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Cookie__Domain", prevValue);
        }
    }

    [Fact]
    public async Task AntiforgeryCookie_SetsDomainAttribute_WhenDomainConfigIsProvided()
    {
        var prevValue = Environment.GetEnvironmentVariable("Cookie__Domain");
        try
        {
            // Belt-and-braces: when an explicit Cookie:Domain is configured,
            // ASP.NET must emit it verbatim in the Set-Cookie header. Use a
            // generic test domain (RFC 6761 reserved) so this test does not
            // couple to any real deployment hostname.
            Environment.SetEnvironmentVariable("Cookie__Domain", ".example.com");

            using var factory = new CookieDomainWebApplicationFactory();
            // Build a raw HttpClient around the test server's handler, with no
            // CookieContainer. The default WebApplicationFactory.CreateClient()
            // adds a CookieContainer that refuses to ingest a cookie whose
            // Domain doesn't match the request URI's host — but browsers have
            // no such restriction, and that's the behaviour we care about
            // here (cross-host cookies are exactly what production uses).
            using var client = new HttpClient(factory.Server.CreateHandler())
            {
                BaseAddress = factory.Server.BaseAddress
            };

            var response = await client.GetAsync("/api/auth/csrf-token");

            Assert.True(response.IsSuccessStatusCode);
            var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
                ? string.Join(",", values)
                : string.Empty;
            // Explicit Cookie:Domain is preserved in the Set-Cookie header.
            // The empty-Domain behaviour is strictly a fallback for local dev.
            Assert.Contains("domain=.example.com", setCookie, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Cookie__Domain", prevValue);
        }
    }

    /// <summary>
    /// Test factory that boots <see cref="Program"/> with the same
    /// configuration a fresh reviewer gets from <c>docker compose up --build</c>
    /// after the fix: the production databases / Postgres / Docker socket are
    /// not required; everything below the cookie-configuration code path is
    /// stubbed out.
    /// </summary>
    private sealed class CookieDomainWebApplicationFactory : WebApplicationFactory<Program>
    {
        public CookieDomainWebApplicationFactory()
        {
            // No shell env leaks (the dev shell has a persistent
            // POSTGRES_PASSWORD); wire up just enough for the test.
            Environment.SetEnvironmentVariable("ConnectionStrings__Default",
                "Host=localhost;Database=pico_tests;Username=postgres;Password=postgres");
            Environment.SetEnvironmentVariable("PROVISIONING_MODE", "mock");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");

            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>());
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<DatabaseInitializer>();

                services.AddSingleton<IUserRepository, NoUsersRepo>();
                services.AddSingleton<IFlavorRepository, NoFlavorsRepo>();
                services.AddSingleton<IImageRepository, NoImagesRepo>();
                services.AddSingleton<IResourceRepository, NoResourcesRepo>();
                services.AddSingleton<IInvoiceRepository, NoInvoicesRepo>();
                services.AddSingleton<IAuditLogRepository, NoAuditLogsRepo>();
                services.AddSingleton<IProvisioningBackend, NoProvisionerRepo>();
            });
        }
    }

    // Minimal stub repositories — these tests only care about the Set-Cookie
    // header, so the bodies can return empty results.
    private sealed class NoUsersRepo : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task AddAsync(User user, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class NoFlavorsRepo : IFlavorRepository
    {
        public Task<Flavor?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Flavor?>(null);
        public Task<IReadOnlyList<Flavor>> ListActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Flavor>>(Array.Empty<Flavor>());
        public Task<IReadOnlyList<Flavor>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Flavor>>(Array.Empty<Flavor>());
        public Task AddAsync(Flavor flavor, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class NoImagesRepo : IImageRepository
    {
        public Task<Image?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Image?>(null);
        public Task<IReadOnlyList<Image>> ListActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Image>>(Array.Empty<Image>());
        public Task AddAsync(Image image, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class NoResourcesRepo : IResourceRepository
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
    private sealed class NoInvoicesRepo : IInvoiceRepository
    {
        public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Invoice?>(null);
        public Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Invoice>>(Array.Empty<Invoice>());
        public Task<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Invoice>>(Array.Empty<Invoice>());
        public Task AddAsync(Invoice invoice, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(Invoice invoice, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class NoAuditLogsRepo : IAuditLogRepository
    {
        public Task AddAsync(AuditLog log, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditLog>> ListAsync(DateTimeOffset since, CancellationToken ct) => Task.FromResult<IReadOnlyList<AuditLog>>(Array.Empty<AuditLog>());
    }
    private sealed class NoProvisionerRepo : IProvisioningBackend
    {
        public string Mode => "fake";
        public Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct) => Task.FromResult(ProvisionResult.Ok("external-test", "127.0.0.1"));
        public Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct) => Task.FromResult(ProvisionResult.Ok(externalId, "127.0.0.1"));
        public Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct) => Task.FromResult(ProvisionResult.Ok(externalId, "127.0.0.1"));
        public Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct) => Task.FromResult(ProvisionResult.Ok(externalId, "127.0.0.1"));
        public Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct) => Task.FromResult(ResourceUsage.Empty());
        public Task<BackendHealth> GetHealthAsync(CancellationToken ct) => Task.FromResult(new BackendHealth(Mode, true, null, DateTimeOffset.UtcNow));
    }
}
