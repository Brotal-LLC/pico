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
using Microsoft.EntityFrameworkCore;
using Pico.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ─────────────────────────────────────────────────────────
builder.Configuration.AddEnvironmentVariables();

// ─── CORS ────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", p => p
        .WithOrigins(allowedOrigins.Length > 0 ? allowedOrigins : new[] { "http://localhost:3000" })
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()
        .SetIsOriginAllowedToAllowWildcardSubdomains());
});

// ─── Persistence ─────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings__Default not configured");
builder.Services.AddDbContext<PicoDbContext>(opts => opts.UseNpgsql(connectionString));
builder.Services.AddDbContextFactory<PicoDbContext>(opts => opts.UseNpgsql(connectionString));

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
builder.Services.AddSingleton<OpenStackOptions>();
builder.Services.AddHttpClient("openstack");
builder.Services.AddScoped<Lazy<DockerProvisioningBackend>>();
builder.Services.AddScoped<Lazy<OpenStackProvisioningBackend>>();
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

// ─── OpenAPI + structured logging ─────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ─── Build & configure pipeline ──────────────────────────────────────────
var app = builder.Build();

app.UseCors("Default");
app.UseAuthentication();
app.UseAuthorization();

// Global error handler — converts to ProblemDetails (RFC 7807)
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception");
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "Internal Server Error",
                status = 500,
                detail = app.Environment.IsDevelopment() ? ex.Message : "An unexpected error occurred"
            });
        }
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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