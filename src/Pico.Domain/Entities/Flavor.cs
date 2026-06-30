namespace Pico.Domain.Entities;

/// <summary>
/// VM package (flavor): CPU, RAM, disk, price. Customers select a flavor when provisioning.
/// </summary>
public class Flavor
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Vcpus { get; private set; }
    public int RamMb { get; private set; }
    public int DiskGb { get; private set; }
    public decimal PricePerHour { get; private set; }
    public decimal PricePerMonth { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public bool Active { get; private set; } = true;

    private Flavor() { }

    public static Flavor Create(
        string name,
        int vcpus,
        int ramMb,
        int diskGb,
        decimal pricePerHour,
        decimal pricePerMonth,
        string category)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (vcpus < 1)
            throw new ArgumentException("vCPUs must be >= 1.", nameof(vcpus));
        if (ramMb < 1)
            throw new ArgumentException("RAM must be >= 1 MB.", nameof(ramMb));
        if (diskGb < 1)
            throw new ArgumentException("Disk must be >= 1 GB.", nameof(diskGb));
        if (pricePerHour <= 0)
            throw new ArgumentException("Price per hour must be positive.", nameof(pricePerHour));
        if (pricePerMonth <= 0)
            throw new ArgumentException("Price per month must be positive.", nameof(pricePerMonth));

        return new Flavor
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Vcpus = vcpus,
            RamMb = ramMb,
            DiskGb = diskGb,
            PricePerHour = pricePerHour,
            PricePerMonth = pricePerMonth,
            Category = (category ?? string.Empty).Trim(),
            Active = true,
        };
    }

    public void Deactivate() => Active = false;
    public void Activate() => Active = true;

    public void UpdatePrice(decimal pricePerHour, decimal pricePerMonth)
    {
        if (pricePerHour <= 0)
            throw new ArgumentException("Price per hour must be positive.", nameof(pricePerHour));
        if (pricePerMonth <= 0)
            throw new ArgumentException("Price per month must be positive.", nameof(pricePerMonth));
        PricePerHour = pricePerHour;
        PricePerMonth = pricePerMonth;
    }
}
