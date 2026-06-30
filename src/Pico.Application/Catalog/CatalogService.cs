using Pico.Application.Common;

namespace Pico.Application.Catalog;

public record FlavorSummaryDto(
    Guid Id,
    string Name,
    int Vcpus,
    int RamMb,
    int DiskGb,
    decimal PricePerHour,
    decimal PricePerMonth,
    string Category
);

public record ImageSummaryDto(
    Guid Id,
    string Name,
    string Os,
    string Version,
    int SizeGb
);

/// <summary>
/// Reads the catalog. Cacheable. Pure DB reads, no auth required.
/// </summary>
public class CatalogService
{
    private readonly IFlavorRepository _flavors;
    private readonly IImageRepository _images;

    public CatalogService(IFlavorRepository flavors, IImageRepository images)
    {
        _flavors = flavors;
        _images = images;
    }

    public async Task<IReadOnlyList<FlavorSummaryDto>> ListFlavorsAsync(CancellationToken ct)
    {
        var flavors = await _flavors.ListActiveAsync(ct);
        return flavors
            .Select(f => new FlavorSummaryDto(
                f.Id, f.Name, f.Vcpus, f.RamMb, f.DiskGb,
                f.PricePerHour, f.PricePerMonth, f.Category))
            .ToList();
    }

    public async Task<FlavorSummaryDto?> GetFlavorAsync(Guid id, CancellationToken ct)
    {
        var f = await _flavors.FindByIdAsync(id, ct);
        return f is null
            ? null
            : new FlavorSummaryDto(
                f.Id, f.Name, f.Vcpus, f.RamMb, f.DiskGb,
                f.PricePerHour, f.PricePerMonth, f.Category);
    }

    public async Task<IReadOnlyList<ImageSummaryDto>> ListImagesAsync(CancellationToken ct)
    {
        var images = await _images.ListActiveAsync(ct);
        return images
            .Select(i => new ImageSummaryDto(i.Id, i.Name, i.Os, i.Version, i.SizeGb))
            .ToList();
    }
}
