namespace Pico.Domain.Entities;

/// <summary>
/// OS image available for provisioning. Mirrors OpenStack Glance image concept.
/// </summary>
public class Image
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Os { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public int SizeGb { get; private set; }
    public bool Active { get; private set; } = true;

    private Image() { }

    public static Image Create(string name, string os, string version, int sizeGb)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (sizeGb < 1)
            throw new ArgumentException("Size must be >= 1 GB.", nameof(sizeGb));

        return new Image
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Os = (os ?? string.Empty).Trim(),
            Version = (version ?? string.Empty).Trim(),
            SizeGb = sizeGb,
            Active = true,
        };
    }

    public void Deactivate() => Active = false;
    public void Activate() => Active = true;
}
