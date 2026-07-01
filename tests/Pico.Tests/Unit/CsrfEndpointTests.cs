using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Infrastructure;

namespace Pico.Tests.Unit;

public class CsrfEndpointTests : IClassFixture<CsrfEndpointTests.CsrfWebApplicationFactory>
{
    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly CsrfWebApplicationFactory _factory;

    public CsrfEndpointTests(CsrfWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CsrfTokenEndpoint_AllowsAnonymousClients_AndIssuesToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/csrf-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
    }

    [Fact]
    public async Task AuthenticatedPostResources_WithoutCsrfToken_IsRejected()
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resources")
        {
            Content = JsonContent.Create(new
            {
                name = "test-resource",
                flavorId = Guid.NewGuid(),
                imageId = Guid.NewGuid()
            })
        };
        request.Headers.Add(TestAuthHandler.UserHeader, TestUserId.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedPostInvoicePay_WithoutCsrfToken_IsRejected()
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{Guid.NewGuid()}/pay")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add(TestAuthHandler.UserHeader, TestUserId.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedDeleteResource_WithoutCsrfToken_IsRejected()
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/resources/{Guid.NewGuid()}");
        request.Headers.Add(TestAuthHandler.UserHeader, TestUserId.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedPostResources_WithBadCsrfToken_IsRejected()
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resources")
        {
            Content = JsonContent.Create(new
            {
                name = "test-resource",
                flavorId = Guid.NewGuid(),
                imageId = Guid.NewGuid()
            })
        };
        request.Headers.Add(TestAuthHandler.UserHeader, TestUserId.ToString());
        request.Headers.Add("X-CSRF-TOKEN", "not-a-valid-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedPostResources_WithCsrfToken_ReachesBusinessValidation()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, TestUserId.ToString());
        var tokenResponse = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf-token");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resources")
        {
            Content = JsonContent.Create(new
            {
                name = "test-resource",
                flavorId = Guid.NewGuid(),
                imageId = Guid.NewGuid()
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", tokenResponse!.Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SafeGetEndpoints_DoNotRequireCsrfToken()
    {
        using var client = _factory.CreateClient();

        var healthResponse = await client.GetAsync("/api/health");
        var flavorsResponse = await client.GetAsync("/api/catalog/flavors");

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, flavorsResponse.StatusCode);
    }

    public sealed class CsrfWebApplicationFactory : WebApplicationFactory<Program>
    {
        public CsrfWebApplicationFactory()
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Host=localhost;Database=pico_tests;Username=postgres;Password=postgres");
            Environment.SetEnvironmentVariable("PROVISIONING_MODE", "mock");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
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

                services.AddSingleton<IUserRepository, FakeUserRepository>();
                services.AddSingleton<IFlavorRepository, FakeFlavorRepository>();
                services.AddSingleton<IImageRepository, FakeImageRepository>();
                services.AddSingleton<IResourceRepository, FakeResourceRepository>();
                services.AddSingleton<IInvoiceRepository, FakeInvoiceRepository>();
                services.AddSingleton<IAuditLogRepository, FakeAuditLogRepository>();
                services.AddSingleton<IProvisioningBackend, FakeProvisioningBackend>();

                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                        options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                        options.DefaultForbidScheme = TestAuthHandler.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        }
    }

    public sealed record CsrfTokenResponse(string Token);

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "TestAuth";
        public const string UserHeader = "X-Test-User";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var values) ||
                !Guid.TryParse(values.FirstOrDefault(), out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = Request.Headers.TryGetValue("X-Test-Role", out var roles)
                ? roles.FirstOrDefault() ?? UserRole.Customer.ToString()
                : UserRole.Customer.ToString();

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, "csrf-test@example.com"),
                new Claim(ClaimTypes.Name, "CSRF Test User"),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, Scheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task AddAsync(User user, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeFlavorRepository : IFlavorRepository
    {
        public Task<Flavor?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Flavor?>(null);
        public Task<IReadOnlyList<Flavor>> ListActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Flavor>>(Array.Empty<Flavor>());
        public Task<IReadOnlyList<Flavor>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Flavor>>(Array.Empty<Flavor>());
        public Task AddAsync(Flavor flavor, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeImageRepository : IImageRepository
    {
        public Task<Image?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Image?>(null);
        public Task<IReadOnlyList<Image>> ListActiveAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Image>>(Array.Empty<Image>());
        public Task AddAsync(Image image, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeResourceRepository : IResourceRepository
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

    private sealed class FakeInvoiceRepository : IInvoiceRepository
    {
        public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Invoice?>(null);
        public Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Invoice>>(Array.Empty<Invoice>());
        public Task<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Invoice>>(Array.Empty<Invoice>());
        public Task AddAsync(Invoice invoice, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(Invoice invoice, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog log, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditLog>> ListAsync(DateTimeOffset since, CancellationToken ct) => Task.FromResult<IReadOnlyList<AuditLog>>(Array.Empty<AuditLog>());
    }

    private sealed class FakeProvisioningBackend : IProvisioningBackend
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
