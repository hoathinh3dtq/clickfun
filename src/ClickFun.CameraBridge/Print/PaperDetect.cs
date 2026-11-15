using System.Drawing.Printing;

namespace ClickFun.CameraBridge;

/// <summary>
/// Auto-detection of the DNP paper size (2x6" vs 4x6"). The DS-RX1 in the booth
/// should expose both sizes through its Windows driver; we match by actual
/// dimensions (±tolerance) instead of paper names because DNP driver naming
/// varies by firmware/locale. PaperSizes are hundredths of an inch (1 in = 100).
/// </summary>
public static class PaperDetect
{
    public readonly record struct Size(double Width, double Height);

    public const double UnitPerInch = 100.0;

    /// <summary>Picks "2x6" or "4x6" based on available sizes and image aspect.</summary>
    public static string PickLayout(IReadOnlyList<Size> available, double imageWidth, double imageHeight)
    {
        var aspect = imageWidth / imageHeight;

        var has26 = available.Any(s => Fits(s, 2, 6) || Fits(s, 6, 2));
        var has46 = available.Any(s => Fits(s, 4, 6) || Fits(s, 6, 4));

        if (has26 && !has46) return "2x6";
        if (has46 && !has26) return "4x6";

        var ratio26 = Math.Abs(aspect - 1.0 / 3.0);
        var ratio46 = Math.Abs(aspect - 2.0 / 3.0);
        return ratio26 < ratio46 ? "2x6" : "4x6";

        static bool Fits(Size s, double wIn, double hIn) =>
            Math.Abs(s.Width - wIn * UnitPerInch) <= wIn * UnitPerInch * 0.08 + 2 &&
            Math.Abs(s.Height - hIn * UnitPerInch) <= hIn * UnitPerInch * 0.08 + 2;
    }

    /// <summary>Finds the driver PaperSize closest to the requested layout inches (2x6 or 4x6), or null.</summary>
    public static PaperSize? MatchPaperSize(IEnumerable<PaperSize> driverSizes, string layout)
    {
        var (tw, th) = layout == "2x6" ? (2.0, 6.0) : (4.0, 6.0);

        PaperSize? best = null;
        var bestScore = double.MaxValue;

        foreach (var size in driverSizes)
        {
            foreach (var (w, h) in new[] { (tw, th), (th, tw) })
            {
                var dw = Math.Abs(size.Width - w * UnitPerInch);
                var dh = Math.Abs(size.Height - h * UnitPerInch);
                var score = dw + dh;
                if (score < bestScore && dw <= w * UnitPerInch * 0.08 + 2 && dh <= h * UnitPerInch * 0.08 + 2)
                {
                    bestScore = score;
                    best = size;
                }
            }
        }

        return best;
    }
}
