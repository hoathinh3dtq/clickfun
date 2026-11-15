using Xunit;

namespace ClickFun.CameraBridge.Tests;

public class PaperDetectTests
{
    private static readonly PaperDetect.Size Strip26 = new(200, 600);  // 2x6 in
    private static readonly PaperDetect.Size Photo46 = new(400, 600);  // 4x6 in

    [Fact]
    public void PickLayout_Only26Available_Returns26()
    {
        var sizes = new[] { Strip26 };
        Assert.Equal("2x6", PaperDetect.PickLayout(sizes, 4000, 6000));
    }

    [Fact]
    public void PickLayout_Only46Available_Returns46()
    {
        var sizes = new[] { Photo46 };
        Assert.Equal("4x6", PaperDetect.PickLayout(sizes, 600, 1800));
    }

    [Fact]
    public void PickLayout_PortraitStripImage_Prefers26()
    {
        var sizes = new[] { Strip26, Photo46 };
        Assert.Equal("2x6", PaperDetect.PickLayout(sizes, 600, 1800));  // 1:3
    }

    [Fact]
    public void PickLayout_PhotoImage_Prefers46()
    {
        var sizes = new[] { Strip26, Photo46 };
        Assert.Equal("4x6", PaperDetect.PickLayout(sizes, 4000, 6000));  // 2:3
    }

    [Fact]
    public void PickLayout_LandscapeImage_UsesNearestAspect()
    {
        var sizes = new[] { Strip26, Photo46 };
        Assert.Equal("4x6", PaperDetect.PickLayout(sizes, 6000, 4000));
    }

    [Fact]
    public void PickLayout_MatchesRotatedDimensionsToo()
    {
        var sizes = new[] { new PaperDetect.Size(600, 200), Photo46 };
        Assert.Equal("2x6", PaperDetect.PickLayout(sizes, 600, 1800));
    }
}
