using System.Text.Json;
using Xunit;

namespace ClickFun.CameraBridge.Tests;

public class FitGeometryTests
{
    [Fact]
    public void LandscapePage_LandscapeImage_FitsWithZoomCentered()
    {
        var r = FitGeometry.Compute(4000, 3000, 1200, 600, 1.0, 0, 0);
        Assert.Equal(800, r.DrawWidth);
        Assert.Equal(600, r.DrawHeight);
        Assert.Equal(200, r.PosX);
        Assert.Equal(0, r.PosY);
        Assert.False(r.Rotated90);
    }

    [Fact]
    public void PortraitImageOnLandscapePage_RotatesLogically()
    {
        var r = FitGeometry.Compute(3000, 4000, 1200, 600, 1.0, 0, 0);
        Assert.True(r.Rotated90);
        Assert.Equal(800, r.DrawWidth);  // 4000x3000 logical landscape
        Assert.Equal(600, r.DrawHeight);
    }

    [Fact]
    public void ZoomAndOffset_Applied()
    {
        var r = FitGeometry.Compute(4000, 3000, 1200, 600, 0.5, 10, -5);
        Assert.Equal(400, r.DrawWidth);
        Assert.Equal(300, r.DrawHeight);
        Assert.Equal(410, r.PosX);
        Assert.Equal(145, r.PosY); // (600-300)/2 + (-5)
    }
}

public class SettingsTests
{
    [Fact]
    public void Defaults_ZoomIs098_PreservesAspectStyle()
    {
        var d = SettingsStore.Defaults();
        Assert.Equal(0.98, d.Zoom);
        Assert.Equal(3, d.CountdownSeconds);
        Assert.Equal(1, d.Copies);
    }

    [Fact]
    public void SettingsDto_RoundTripsJson()
    {
        var d = SettingsStore.Defaults() with { PrinterName = "DNP DS-RX1", Copies = 2, OffsetX = 7, OffsetY = -3 };
        var json = JsonSerializer.Serialize(d);
        var back = JsonSerializer.Deserialize<SettingsDto>(json);
        Assert.Equal("DNP DS-RX1", back!.PrinterName);
        Assert.Equal(2, back.Copies);
        Assert.Equal(7, back.OffsetX);
        Assert.Equal(-3, back.OffsetY);
    }
}

public class FilterEngineTests
{
    private static byte[] MakeTestJpeg()
    {
        using var mat = new OpenCvSharp.Mat(40, 40, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(10, 20, 30));
        OpenCvSharp.Cv2.ImEncode(".jpg", mat, out var jpg);
        return jpg;
    }

    [Fact]
    public void NoneFilter_ReturnsInputUnchanged()
    {
        var jpg = MakeTestJpeg();
        var output = FilterEngine.Apply(jpg, "none");
        Assert.Equal(jpg, output);
    }

    [Fact]
    public void GrayscaleFilter_ProducesValidJpeg()
    {
        var jpg = MakeTestJpeg();
        var output = FilterEngine.Apply(jpg, "grayscale");
        using var decoded = OpenCvSharp.Cv2.ImDecode(output, OpenCvSharp.ImreadModes.Color);
        Assert.Equal(40, decoded.Width);
        Assert.Equal(40, decoded.Height);
    }
}
