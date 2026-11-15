namespace ClickFun.CameraBridge;

/// <summary>
/// Pure geometry for the DS-RX1 print pipeline. Ported from the observed
/// behavior of the original photo-booth print script (orientation auto-detect,
/// rotate-90 fallback, fit-while-preserving-aspect with zoom and offsets).
/// Kept free of System.Drawing so it is unit-testable.
/// </summary>
public static class FitGeometry
{
    public readonly record struct Result(
        int DrawWidth,
        int DrawHeight,
        int PosX,
        int PosY,
        bool Rotated90);

    /// <summary>
    /// Computes where to draw an image on the printer page.
    /// </summary>
    /// <param name="imageWidth">Image width in pixels (pre-rotation handling is internal).</param>
    /// <param name="imageHeight">Image height in pixels.</param>
    /// <param name="pageWidth">Printer page bounds in pixels (as reported by the driver).</param>
    /// <param name="pageHeight">Printer page bounds in pixels.</param>
    /// <param name="zoom">User zoom factor (default 0.98 to avoid edge clipping on dye-sub).</param>
    /// <param name="offsetX">User X offset in pixels (printer pixels, not screen pixels).</param>
    /// <param name="offsetY">User Y offset in pixels.</param>
    public static Result Compute(int imageWidth, int imageHeight, int pageWidth, int pageHeight,
        double zoom, int offsetX, int offsetY)
    {
        var imgW = imageWidth;
        var imgH = imageHeight;
        var rotated = false;

        var pageIsLandscape = pageWidth > pageHeight;
        var imageIsLandscape = imgW > imgH;

        // Mirrors the driver-ignores-Landscape workaround: if orientation mismatches,
        // rotate the image 90° logically instead of fighting the page setup.
        if (imageIsLandscape != pageIsLandscape)
        {
            (imgW, imgH) = (imgH, imgW);
            rotated = true;
        }

        var ratio = Math.Min((double)pageWidth / imgW, (double)pageHeight / imgH);
        var newW = (int)(imgW * ratio * zoom);
        var newH = (int)(imgH * ratio * zoom);
        var posX = (int)((pageWidth - newW) / 2.0) + offsetX;
        var posY = (int)((pageHeight - newH) / 2.0) + offsetY;

        return new Result(newW, newH, posX, posY, rotated);
    }
}
