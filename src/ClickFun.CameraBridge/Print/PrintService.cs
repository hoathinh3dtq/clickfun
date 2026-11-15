using System.Drawing;
using System.Drawing.Printing;

namespace ClickFun.CameraBridge;

/// <summary>
/// DS-RX1 print pipeline. .NET port of the original booth's PowerShell script,
/// extended with automatic 2x6" / 4x6" paper detection (driver-driven).
/// Zero margins, StandardPrintController, orientation auto-detect,
/// rotate-90 fallback when the driver ignores orientation, fit-and-center
/// with zoom + offsets, inter-copy delay for dye-sub head recovery.
/// </summary>
public sealed class PrintService
{
    private readonly SettingsStore _settings;

    public PrintService(SettingsStore settings)
    {
        _settings = settings;
    }

    public static bool IsPrinterAvailable(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return PrinterSettings.InstalledPrinters.Count > 0;
        var ps = new PrinterSettings { PrinterName = printerName.Trim() };
        return ps.IsValid;
    }

    public static IReadOnlyList<(string Layout, double WidthInches, double HeightInches)> DetectLayouts(string? printerName)
    {
        var list = new List<(string Layout, double WidthInches, double HeightInches)>();
        if (string.IsNullOrWhiteSpace(printerName)) return list;
        var ps = new PrinterSettings { PrinterName = printerName.Trim() };
        if (!ps.IsValid) return list;
        foreach (PaperSize size in ps.PaperSizes)
        {
            var w = size.Width / 100.0;
            var h = size.Height / 100.0;
            if (Matches2(w, h, 2, 6)) list.Add(("2x6", w, h));
            if (Matches2(w, h, 4, 6)) list.Add(("4x6", w, h));
        }
        return list;

        static bool Matches2(double w, double h, double tw, double th)
        {
            var dw1 = Math.Abs(w - tw); var dh1 = Math.Abs(h - th);
            var dw2 = Math.Abs(w - th); var dh2 = Math.Abs(h - tw);
            return (dw1 <= tw * 0.08 + 0.02 && dh1 <= th * 0.08 + 0.02)
                || (dw2 <= th * 0.08 + 0.02 && dh2 <= tw * 0.08 + 0.02);
        }
    }

    public PrintResult Print(PrintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ImagePath) || !File.Exists(request.ImagePath))
            return new PrintResult(false, $"print file not found: {request.ImagePath}", 0, null);

        var settings = _settings.Load();
        var printerName = string.IsNullOrWhiteSpace(request.PrinterName) ? settings.PrinterName : request.PrinterName!.Trim();
        var copies = request.Copies > 0 ? request.Copies : settings.Copies;
        var zoom = request.Zoom ?? settings.Zoom;
        var offsetX = request.OffsetX ?? settings.OffsetX;
        var offsetY = request.OffsetY ?? settings.OffsetY;
        var layoutRequest = (request.PaperLayout ?? settings.PaperLayout ?? "auto").Trim().ToLowerInvariant();

        try
        {
            using var probe = new Bitmap(request.ImagePath);
            var imageIsLandscape = probe.Width > probe.Height;

            using var doc = new PrintDocument
            {
                PrintController = new StandardPrintController(),
                OriginAtMargins = false,
            };
            doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

            if (!string.IsNullOrWhiteSpace(printerName))
            {
                doc.PrinterSettings.PrinterName = printerName;
                if (!doc.PrinterSettings.IsValid)
                    return new PrintResult(false, $"printer '{printerName}' not found or invalid. Installed: {string.Join(", ", PrinterSettings.InstalledPrinters.Cast<string>())}", 0, null);
            }
            else if (!doc.PrinterSettings.IsValid)
            {
                return new PrintResult(false, "no valid default printer installed", 0, null);
            }

            // ---- Paper layout detection (2x6 vs 4x6, DS-RX1) ----
            var driverSizes = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().ToList();
            var sizes = driverSizes.Select(s => new PaperDetect.Size(s.Width, s.Height)).ToList();
            var layout = layoutRequest is "2x6" or "4x6"
                ? layoutRequest
                : PaperDetect.PickLayout(sizes, probe.Width, probe.Height);

            var paper = PaperDetect.MatchPaperSize(driverSizes, layout);
            if (paper is not null)
            {
                doc.DefaultPageSettings.PaperSize = paper;
                BridgeLog.Info($"Print paper: {paper.PaperName} ({paper.Width}x{paper.Height}/100in) layout={layout}");
            }
            else if (layoutRequest is "2x6" or "4x6")
            {
                return new PrintResult(false, $"printer has no {layout} paper size available; available: {string.Join(", ", driverSizes.Select(s => s.PaperName))}", 0, null);
            }
            else
            {
                BridgeLog.Warn("No 2x6 or 4x6 paper size found on the printer; using driver default.");
            }

            var pageLandscape = paper is not null
                ? paper.Width > paper.Height
                : imageIsLandscape;
            doc.DefaultPageSettings.Landscape = pageLandscape;

            var rotate90 = false;
            Exception? renderError = null;
            var printed = 0;

            doc.PrintPage += (_, e) =>
            {
                try
                {
                    using var img = new Bitmap(request.ImagePath);
                    var imgW = img.Width;
                    var imgH = img.Height;

                    if (imgIsLandscapeSafe(img) != PageIsLandscape(e))
                    {
                        img.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        imgW = img.Width;
                        imgH = img.Height;
                        rotate90 = true;
                    }

                    var geo = FitGeometry.Compute(imgW, imgH, e.PageBounds.Width, e.PageBounds.Height, zoom, offsetX, offsetY);

                    e!.Graphics!.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    e.Graphics.DrawImage(img, geo.PosX, geo.PosY, geo.DrawWidth, geo.DrawHeight);
                }
                catch (Exception ex)
                {
                    renderError = ex;
                    e.Cancel = true;
                }
            };

            for (var i = 0; i < copies; i++)
            {
                doc.Print();
                var err = renderError;
                if (err is not null)
                    return new PrintResult(false, $"render failed on copy {i + 1}: {err.Message}", printed, layout);
                printed++;
                if (i < copies - 1) Thread.Sleep(2500); // dye-sub head recovery
            }

            BridgeLog.Info($"Printed {printed} copie(s) of {request.ImagePath} (layout={layout}, rotate90={rotate90}, zoom={zoom}, offset=({offsetX},{offsetY}))");
            return new PrintResult(true, $"printed {printed} copie(s) on {layout}", printed, layout);
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"Print failed for {request.ImagePath}", ex);
            return new PrintResult(false, ex.Message, 0, null);
        }

        static bool imgIsLandscapeSafe(Image img) => img.Width > img.Height;
        static bool PageIsLandscape(PrintPageEventArgs e) => e.PageBounds.Width > e.PageBounds.Height;
    }
}
