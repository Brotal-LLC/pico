using Pico.Application.Catalog;

namespace Pico.Api.Endpoints;

/// <summary>
/// Public catalog endpoints — browsing flavors/images requires no auth,
/// but auth-gated endpoints (provision, etc.) reference these by id.
/// </summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog");

        group.MapGet("/flavors", async (CatalogService svc, CancellationToken ct) =>
        {
            var flavors = await svc.ListFlavorsAsync(ct);
            return Results.Ok(flavors);
        });

        group.MapGet("/flavors/{id:guid}", async (Guid id, CatalogService svc, CancellationToken ct) =>
        {
            var flavor = await svc.GetFlavorAsync(id, ct);
            return flavor is null ? Results.NotFound() : Results.Ok(flavor);
        });

        group.MapGet("/images", async (CatalogService svc, CancellationToken ct) =>
        {
            var images = await svc.ListImagesAsync(ct);
            return Results.Ok(images);
        });

        return app;
    }
}
