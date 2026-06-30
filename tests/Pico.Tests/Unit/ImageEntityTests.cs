using Pico.Domain.Entities;

namespace Pico.Tests.Unit;

public class ImageEntityTests
{
    [Fact]
    public void Create_WithValidArgs_ReturnsImage()
    {
        var image = Image.Create("ubuntu-24.04", "Ubuntu", "24.04 LTS", 2);
        Assert.NotEqual(Guid.Empty, image.Id);
        Assert.Equal("ubuntu-24.04", image.Name);
        Assert.Equal("Ubuntu", image.Os);
        Assert.Equal("24.04 LTS", image.Version);
        Assert.Equal(2, image.SizeGb);
        Assert.True(image.Active);
    }

    [Fact]
    public void Create_WithBlankName_Throws() =>
        Assert.Throws<ArgumentException>(() => Image.Create("", "Ubuntu", "24.04", 2));

    [Fact]
    public void Create_WithNonPositiveSize_Throws() =>
        Assert.Throws<ArgumentException>(() => Image.Create("ubuntu", "Ubuntu", "24.04", 0));

    [Fact]
    public void Deactivate_MarksInactive()
    {
        var image = Image.Create("ubuntu", "Ubuntu", "24.04", 2);
        image.Deactivate();
        Assert.False(image.Active);
    }
}