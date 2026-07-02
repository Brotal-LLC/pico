using Pico.Application.Billing;
using Pico.Application.Catalog;
using Pico.Application.Common;
using Pico.Application.Provisioning;
using Pico.Application.Resources;
using Pico.Infrastructure;
using Pico.Infrastructure.Persistence;
using Pico.Infrastructure.Provisioning;
using Pico.Infrastructure.Repositories;
using Pico.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Pico.Api.Endpoints;
using Pico.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ─────────────────────────────────────────────────────────
builder.Configuration.AddEnvironmentVariables();

// Helper: ASP.NET's CookieBuilder emits `domain=;` (empty Domain attribute)
// when the config value is an empty string instead of omitting the
// attribute. Some browsers reject or mistreat that header, which can break
// the antiforgery / auth cookie flow in local dev. Returns null when the
// config value is null, missing, or whitespace, so the Domain attribute is
// omitted and cookies are scoped to the exact host that issued them.
static string? GetCookieDomain(IConfiguration config) =>
    string.IsNullOrWhiteSpace(config["Cookie:Domain"]) ? null : config["Cookie:Domain"];

// ─── Reverse proxy headers (Caddy / Cloudflare) ──────────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ─── CORS ────────────────────────────────────────────────────────────────
var corsOriginsConfig = builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:3000";
var allowedOrigins = corsOriginsConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", p => p
        .WithOrigins(allowedOrigins)
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()
        .SetIsOriginAllowedToAllowWildcardSubdomains());
});

// ─── Persistence ─────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings__Default not configured");
// EnableRetryOnFailure wraps the EF Core execution strategy in Npgsql's
// built-in retry policy. On cold starts the Docker embedded DNS resolver
// (127.0.0.11) can briefly return EAGAIN to a host lookup, which would
// otherwise surface as a fatal `Resource temporarily unavailable` from
// `System.Net.Dns.GetHostEntryOrAddressesCore` during the very first
// `MigrateAsync` call. The retry strategy transparently handles
// transient connection-level failures during the lifetime of the app.
builder.Services.AddDbContext<PicoDbContext>(opts => opts.UseNpgsql(
    connectionString,
    npg => npg.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(10),
        errorCodesToAdd: null)));

// ─── Rate limiting (Gap #S2 from AUDIT_REPORT.md) ────────────────────────
// 5 login/signup attempts per 15 minutes per IP. Rejects with 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-ip", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

// ─── Password hashing ────────────────────────────────────────────────────
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

// ─── Repositories ────────────────────────────────────────────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IFlavorRepository, FlavorRepository>();
builder.Services.AddScoped<IImageRepository, ImageRepository>();
builder.Services.AddScoped<IResourceRepository, ResourceRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// ─── Provisioning backends + factory ─────────────────────────────────────
builder.Services.AddScoped<MockProvisioningBackend>();
builder.Services.AddScoped<DockerProvisioningBackend>();
builder.Services.Configure<OpenStackOptions>(builder.Configuration.GetSection("OpenStack"));
builder.Services.AddHttpClient("openstack");
builder.Services.AddScoped<OpenStackProvisioningBackend>();
builder.Services.AddScoped<ProvisioningBackendFactory>();

builder.Services.AddScoped<IProvisioningBackend>(sp =>
    sp.GetRequiredService<ProvisioningBackendFactory>().Resolve(
        sp.GetRequiredService<IConfiguration>()["PROVISIONING_MODE"]));

// ─── Application services ────────────────────────────────────────────────
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<ResourceService>();
builder.Services.AddScoped<InvoiceGenerator>();
builder.Services.AddScoped<InvoiceGenerationService>();

// ─── Seed + auto-migrate ────────────────────────────────────────────────
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddHostedService<DatabaseInitializer>();

// ─── Cookie auth ─────────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Pico.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.Domain = GetCookieDomain(builder.Configuration);
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "Pico.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.Domain = GetCookieDomain(builder.Configuration);
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ─── Problem Details (RFC 7807) ──────────────────────────────────────────
builder.Services.AddProblemDetails();

// ─── JSON serialization: enums as strings ────────────────────────────────
// DTOs that expose `UserRole` (and any future enum) must serialize with
// their .NET names so the SPA's string-based comparisons keep working.
// Without this the wire format is `"role": 1` and role gating silently fails.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// ─── OpenAPI ──────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ─── Build & configure pipeline ──────────────────────────────────────────
var app = builder.Build();

app.UseForwardedHeaders();
app.UseCors("Default");

app.UseAuthentication();
app.UseAuthorization();

// Security response headers (HSTS only emitted over HTTPS, see middleware).
app.UseSecurityHeaders(isHttpsOnly: !builder.Environment.IsEnvironment("Testing"));

app.UseRateLimiter();

// Global exception handler → ProblemDetails
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ─── CSRF token endpoint ─────────────────────────────────────────────────
app.MapGet("/api/auth/csrf-token", (Microsoft.AspNetCore.Antiforgery.IAntiforgery af, HttpContext ctx) =>
{
    var tokens = af.GetAndStoreTokens(ctx);
    return Results.Ok(new { token = tokens.RequestToken });
}).AllowAnonymous();

// ─── Health ──────────────────────────────────────────────────────────────
app.MapGet("/api/health", async (IProvisioningBackend backend) =>
{
    var backendHealth = await backend.GetHealthAsync(CancellationToken.None);
    return Results.Ok(new
    {
        status = "ok",
        backend = backendHealth.Mode,
        backendHealthy = backendHealth.Healthy,
        timestamp = DateTimeOffset.UtcNow
    });
});

// ─── Auth endpoints ──────────────────────────────────────────────────────
app.MapAuthEndpoints();

// ─── Catalog endpoints (public) ──────────────────────────────────────────
app.MapCatalogEndpoints();

// ─── Resource endpoints (auth required) ──────────────────────────────────
app.MapResourceEndpoints();

// ─── Invoice endpoints (auth required) ───────────────────────────────────
app.MapInvoiceEndpoints();

// ─── Admin endpoints (admin role required) ───────────────────────────────
app.MapAdminEndpoints();

app.Run();

public partial class Program { }