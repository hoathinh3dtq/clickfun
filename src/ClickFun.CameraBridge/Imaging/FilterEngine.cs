using OpenCvSharp;

namespace ClickFun.CameraBridge;

public static class FilterEngine
{
    /// <summary>Applies the named filter to a JPEG buffer. Unknown/None returns the input unchanged.</summary>
    public static byte[] Apply(byte[] jpeg, string filter)
    {
        var kind = filter?.Trim().ToLowerInvariant() switch
        {
            "grayscale" or "gray" => 1,
            "sepia" => 2,
            "vintage" or "retro" => 3,
            _ => 0,
        };

        if (kind == 0) return jpeg;

        using var src = Cv2.ImDecode(jpeg, ImreadModes.Color);
        if (src.Empty()) return jpeg;
        using var dst = new Mat();

        switch (kind)
        {
            case 1:
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(dst, dst, ColorConversionCodes.GRAY2BGR);
                break;

            case 2:
                using (var sepiaMat = new Mat(3, 3, MatType.CV_64FC1, new double[]
                {
                    0.272, 0.349, 0.393,
                    0.534, 0.686, 0.769,
                    0.131, 0.168, 0.189,
                }))
                {
                    Cv2.Transform(src, dst, sepiaMat);
                }
                break;

            case 3:
                var lut = new int[256];
                for (var i = 0; i < 256; i++) lut[i] = Math.Clamp(i * 92 / 100 + 24, 0, 255);
                using (var lutMat = new Mat(1, 256, MatType.CV_8UC1, lut))
                using (var adjusted = new Mat())
                {
                    Cv2.LUT(src, lutMat, adjusted);
                    Cv2.ConvertScaleAbs(adjusted, dst, alpha: 0.96, beta: 8);
                }
                break;
        }

        Cv2.ImEncode(".jpg", dst, out var encoded, new[] { (int)ImwriteFlags.JpegQuality, 90 });
        return encoded;
    }

    /// <summary>
    /// Composites a transparent PNG overlay (template frame) onto the frame, resizing to
    /// the base image size. Binary alpha (mask != 0) is used — soft-edged overlays may show
    /// slightly hard edges; acceptable for v0.1 templates that are hard-alpha artwork.
    /// Returns the input unchanged when the template is missing/undecodable.
    /// </summary>
    public static byte[] ApplyOverlay(byte[] jpeg, string overlayPath)
    {
        if (string.IsNullOrWhiteSpace(overlayPath) || !File.Exists(overlayPath))
            return jpeg;

        try
        {
            using var baseImg = Cv2.ImDecode(jpeg, ImreadModes.Color);
            using var overlay = Cv2.ImRead(overlayPath, ImreadModes.Unchanged);
            if (baseImg.Empty() || overlay.Empty() || overlay.Channels() != 4)
            {
                BridgeLog.Warn($"Overlay skipped (decode failed or not RGBA): {overlayPath}");
                return jpeg;
            }

            using var resized = new Mat();
            Cv2.Resize(overlay, resized, baseImg.Size(), 0, 0, InterpolationFlags.Linear);
            var channels = resized.Split();
            using var alpha = channels[3];
            for (var i = 0; i < 3; i++) channels[i].Dispose();
            using var foreground = new Mat();
            Cv2.CvtColor(resized, foreground, ColorConversionCodes.BGRA2BGR);

            foreground.CopyTo(baseImg, alpha);

            Cv2.ImEncode(".jpg", baseImg, out var encoded, new[] { (int)ImwriteFlags.JpegQuality, 92 });
            return encoded;
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"Overlay failed for {overlayPath}", ex);
            return jpeg;
        }
    }
}
