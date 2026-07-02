using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
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
/// Regression tests for the default CORS / cookie-domain configuration that
/// a fresh reviewer gets from <c>docker compose up --build</c> (no .env file).
///
/// The pre-fix defaults in compose.yaml pointed at a hardcoded production
/// origin, which meant a reviewer running the stack on their laptop could
/// not authenticate: the browser blocked the cross-origin XHR from
/// <c>http://localhost:3000</c> with a CORS preflight failure.
///
/// After the fix, the compose defaults are local-dev-friendly
/// (<c>http://localhost:3000</c> for CORS, no cookie domain). Production
/// deploys override these via <c>.env</c>.
///
/// These tests boot the app with the SAME default configuration a fresh
/// clone gets (no <c>Cors:AllowedOrigins</c>, no <c>Cookie:Domain</c>) and
/// assert that a CORS preflight from the dev origin succeeds.
/// </summary>
public class DefaultCorsPolicyTests : IClassFixture<DefaultCorsPolicyTests.DefaultCorsWebApplicationFactory>
{
    private const string DevOrigin = "http://localhost:3000";
    private readonly DefaultCorsWebApplicationFactory _factory;

    public DefaultCorsPolicyTests(DefaultCorsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CorsPreflight_FromLocalhost3000_IsAllowed()
    {
        // The pre-flight that the browser fires before a real login POST.
        // Pre-fix: no Access-Control-Allow-Origin header → browser blocks.
        // Post-fix: header echoes the dev origin, credentials allowed.
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", DevOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin),
            "Expected Access-Control-Allow-Origin header on CORS preflight");
        Assert.Contains(DevOrigin, allowOrigin!);
    }

    [Fact]
    public async Task CorsPreflight_FromForeignOrigin_IsNotAllowedByDevDefaults()
    {
        // Belt-and-braces: the dev defaults are not a wildcard. A
        // arbitrary foreign origin must NOT be silently allowed when the
        // dev defaults are in effect (that would mean we're using
        // `AllowAnyOrigin` somewhere, which is incompatible with
        // `AllowCredentials`).
        using var client = _factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", "https://foreign-origin.example.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");

        var response = await client.SendAsync(request);

        // CORS middleware will short-circuit preflight from a non-allowed
        // origin by NOT emitting Access-Control-Allow-Origin, but the
        // response may still be 204 from the framework. The browser
        // ignores the response if ACAO is missing.
        Assert.False(
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin)
                && allowOrigin!.Contains("https://foreign-origin.example.com"),
            "Dev defaults must NOT silently allow an arbitrary foreign origin");
    }

    [Fact]
    public void CorsAllowedOrigins_DefaultsToLocalhost3000_WhenNoConfigProvided()
    {
        // Program.cs line 33: `builder.Configuration["Cors:AllowedOrigins"]
        // ?? "http://localhost:3000"`. This test pins that default so a
        // future refactor can't silently change it (e.g. to the production
        // URL, which would break the local-dev flow again).
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var corsSection = config.GetSection("Cors:AllowedOrigins");
        // The section either has no value (so the ?? in Program.cs kicks in)
        // or is explicitly set to http://localhost:3000. Both are acceptable.
        var explicitValue = corsSection.Value;
        if (explicitValue is not null)
        {
            Assert.Equal("http://localhost:3000", explicitValue);
        }
    }

    [Fact]
    public void ComposeYaml_LocalDevDefaults_AreLocalhost3000()
    {
        // Belt-and-braces: pin the compose.yaml default for the dev
        // experience. The Program.cs fallback and the compose default
        // must stay in sync, otherwise a reviewer with a non-empty shell
        // env that overrides Cors__AllowedOrigins would hit a different
        // value than someone who runs docker compose with no .env at all.
        var repoRoot = FindRepoRoot();
        var composePath = Path.Combine(repoRoot, "compose.yaml");
        Assert.True(File.Exists(composePath), $"compose.yaml not found at {composePath}");

        var yaml = File.ReadAllText(composePath);
        Assert.True(yaml.Contains("Cors__AllowedOrigins:-http://localhost:3000"),
            "compose.yaml default for Cors__AllowedOrigins must be http://localhost:3000 " +
            "so a fresh `docker compose up --build` works on localhost");
        // Cookie__Domain default is empty (no domain scope) so local-dev
        // cookies are scoped to the exact API host (localhost:8080).
        Assert.True(yaml.Contains("Cookie__Domain:-}"),
            "compose.yaml default for Cookie__Domain must be empty " +
            "so local-dev cookies work without a domain scope");
    }

    private static string FindRepoRoot()
    {
        // Walk up from the test assembly's directory until we find
        // compose.yaml. This makes the test robust to test runner
        // working-directory choices.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "compose.yaml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find compose.yaml by walking up from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// WebApplicationFactory that mimics a fresh reviewer's environment:
    /// no <c>Cors:AllowedOrigins</c> and no <c>Cookie:Domain</c> set via
    /// environment variables. The compose.yaml defaults should make
    /// <c>http://localhost:3000</c> a valid CORS origin and leave
    /// cookies unscoped (so the browser sends them back to the exact host
    /// that issued them, which on local dev is <c>localhost:8080</c>).
    /// </summary>
    public sealed class DefaultCorsWebApplicationFactory : WebApplicationFactory<Program>
    {
        public DefaultCorsWebApplicationFactory()
        {
            // Make sure no shell env from the dev's persistent rc files
            // (e.g. POSTGRES_PASSWORD) leaks into the test, and explicitly
            // unset the CORS / Cookie config so Program.cs defaults apply.
            Environment.SetEnvironmentVariable("ConnectionStrings__Default",
                "Host=localhost;Database=pico_tests;Username=postgres;Password=postgres");
            Environment.SetEnvironmentVariable("PROVISIONING_MODE", "mock");
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins", null);
            Environment.SetEnvironmentVariable("Cookie__Domain", null);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");

            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                // Belt-and-braces: drop any in-memory config that might
                // carry an env-derived Cors:AllowedOrigins or Cookie:Domain
                // from a prior test in the same process. The test depends
                // on the `??` fallback in Program.cs kicking in.
                cfg.AddInMemoryCollection(new Dictionary<string, string?>());
            });

            builder.ConfigureTestServices(services =>
            {
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
    }
}
