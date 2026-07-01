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
using Microsoft.EntityFrameworkCore;
using Pico.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ─────────────────────────────────────────────────────────
builder.Configuration.AddEnvironmentVariables();

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
builder.Services.AddDbContext<PicoDbContext>(opts => opts.UseNpgsql(connectionString));

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

// ─── Seed + auto-migrate ────────────────────────────────────────────────
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddHostedService<DatabaseInitializer>();

// ─── Cookie auth ─────────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Pico.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.Domain = builder.Configuration["Cookie:Domain"];
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
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
    options.Cookie.Domain = builder.Configuration["Cookie:Domain"];
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsEnvironment("Testing")
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
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