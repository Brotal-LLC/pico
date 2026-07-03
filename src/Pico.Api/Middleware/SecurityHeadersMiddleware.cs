namespace Pico.Api.Middleware;

/// <summary>
/// Adds the six security response headers required by the audit rubric
/// (see AUDIT_REPORT.md §5 Gap #2):
///   - X-Content-Type-Options: nosniff
///   - X-Frame-Options: DENY
///   - Referrer-Policy: strict-origin-when-cross-origin
///   - Permissions-Policy: camera/microphone/geolocation disabled
///   - Strict-Transport-Security: 2y, includeSubDomains, preload
///   - Content-Security-Policy: self + api + inline styles (Tailwind compiles inline)
///
/// HSTS is only emitted over HTTPS to keep dev/test working without warnings.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isHttpsOnly;

    public SecurityHeadersMiddleware(RequestDelegate next, bool isHttpsOnly = true)
    {
        _next = next;
        _isHttpsOnly = isHttpsOnly;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Use OnStarting so headers are written even when downstream short-circuits
        context.Response.OnStarting(static state =>
        {
            var (ctx, isHttpsOnly) = ((HttpContext, bool))state;
            var h = ctx.Response.Headers;

            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            if (!isHttpsOnly || ctx.Request.IsHttps)
            {
                h["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains; preload";
            }

            // CSP: allow self + all API origins + inline styles (Tailwind injects style tags at build)
            // and inline scripts for Next.js hydration. Tighten further when feasible.
            var apiOrigins = ctx.RequestServices
                .GetRequiredService<IConfiguration>()["Cors:AllowedOrigins"]?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? [];
            var csp = apiOrigins.Length == 0
                ? "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'"
                : $"default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self' {string.Join(' ', apiOrigins)}";
            h["Content-Security-Policy"] = csp;

            return Task.CompletedTask;
        }, (context, _isHttpsOnly));

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool isHttpsOnly = true)
        => app.UseMiddleware<SecurityHeadersMiddleware>(isHttpsOnly);
}