using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pico.Application.Catalog;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Infrastructure;

namespace Pico.Tests.Unit;

/// <summary>
/// Pins the wire format of UserRole to camelCase enum strings.
/// If someone reverts the JsonStringEnumConverter registration, the
/// admin-role gating in the SPA silently breaks because the integer
/// form (e.g. 1) never equals "Admin". This test fails loudly in CI.
/// </summary>
public class UserRoleSerializationTests : IClassFixture<UserRoleSerializationTests.RoleSerializationFactory>
{
    private readonly RoleSerializationFactory _factory;

    public UserRoleSerializationTests(RoleSerializationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCurrentUser_AsAdmin_SerializesRoleAsString()
    {
        var client = _factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Add(RoleAuthHandler.UserHeader, Guid.NewGuid().ToString());
        req.Headers.Add(RoleAuthHandler.RoleHeader, UserRole.Admin.ToString());

        var resp = await client.SendAsync(req);

        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(raw);
        Assert.True(doc.RootElement.TryGetProperty("role", out var role),
            $"role field missing from /api/auth/me payload: {raw}");

        Assert.Equal(JsonValueKind.String, role.ValueKind);
        Assert.Equal("Admin", role.GetString());
    }

    public class RoleSerializationFactory : WebApplicationFactory<Program>
    {
        public RoleSerializationFactory()
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
                        options.DefaultAuthenticateScheme = RoleAuthHandler.Scheme;
                        options.DefaultChallengeScheme = RoleAuthHandler.Scheme;
                        options.DefaultForbidScheme = RoleAuthHandler.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, RoleAuthHandler>(RoleAuthHandler.Scheme, _ => { });
            });
        }
    }

    private class RoleAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "RoleTestAuth";
        public const string UserHeader = "X-Test-User";
        public const string RoleHeader = "X-Test-Role";

        public RoleAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userValues) ||
                !Guid.TryParse(userValues.FirstOrDefault(), out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = Request.Headers.TryGetValue(RoleHeader, out var roleValues)
                ? roleValues.FirstOrDefault() ?? UserRole.Customer.ToString()
                : UserRole.Customer.ToString();

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, "role-serialization-test@example.com"),
                new Claim(ClaimTypes.Name, "Role Serialization Test"),
                new Claim(ClaimTypes.Role, role),
            };
            var identity = new ClaimsIdentity(claims, Scheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme);
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
        public Task UpdateAsync(Resource resource, CancellationToken ct) => Task.CompletedTask;
        public Task AddEventAsync(ResourceEvent evt, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ResourceEvent>> ListEventsAsync(Guid resourceId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ResourceEvent>>(Array.Empty<ResourceEvent>());
        public Task AddAsync(Resource resource, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeInvoiceRepository : IInvoiceRepository
    {
        public Task<Invoice?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Invoice?>(null);
        public Task<IReadOnlyList<Invoice>> ListByUserAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Invoice>>(Array.Empty<Invoice>());
        public Task<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Invoice>>(Array.Empty<Invoice>());
        public Task UpdateAsync(Invoice invoice, CancellationToken ct) => Task.CompletedTask;
        public Task AddAsync(Invoice invoice, CancellationToken ct) => Task.CompletedTask;
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
        public Task<IShellSession> ExecInteractiveAsync(string externalId, CancellationToken ct) =>
            throw new NotSupportedException("Test stub does not implement interactive exec.");
    }
}
