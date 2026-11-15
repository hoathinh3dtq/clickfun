using System.Drawing;
using System.Drawing.Printing;

namespace ClickFun.CameraBridge;

/// <summary>
/// DNP DS-RX1 print pipeline. .NET port of the original booth's PowerShell script:
/// zero margins, StandardPrintController, landscape auto-detect from the image,
/// rotate-90 fallback when the driver ignores orientation, fit-and-center with
/// zoom + offsets, inter-copy delay for dye-sub head recovery.
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

    public PrintResult Print(PrintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ImagePath) || !File.Exists(request.ImagePath))
            return new PrintResult(false, $"print file not found: {request.ImagePath}", 0);

        var settings = _settings.Load();
        var printerName = string.IsNullOrWhiteSpace(request.PrinterName) ? settings.PrinterName : request.PrinterName!.Trim();
        var copies = request.Copies > 0 ? request.Copies : settings.Copies;
        var zoom = request.Zoom ?? settings.Zoom;
        var offsetX = request.OffsetX ?? settings.OffsetX;
        var offsetY = request.OffsetY ?? settings.OffsetY;

        try
        {
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
                    return new PrintResult(false, $"printer '{printerName}' not found or invalid. Installed: {string.Join(", ", PrinterSettings.InstalledPrinters.Cast<string>())}", 0);
            }
            else if (!doc.PrinterSettings.IsValid)
            {
                return new PrintResult(false, "no valid default printer installed", 0);
            }

            using var probe = new Bitmap(request.ImagePath);
            var imageIsLandscape = probe.Width > probe.Height;
            doc.DefaultPageSettings.Landscape = imageIsLandscape;

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

                    e.Graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
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
                    return new PrintResult(false, $"render failed on copy {i + 1}: {err.Message}", printed);
                printed++;
                if (i < copies - 1) Thread.Sleep(2500); // dye-sub head recovery
            }

            BridgeLog.Info($"Printed {printed} copie(s) of {request.ImagePath} (rotate90={rotate90}, zoom={zoom}, offset=({offsetX},{offsetY}))");
            return new PrintResult(true, $"printed {printed} copie(s)", printed);
        }
        catch (Exception ex)
        {
            BridgeLog.Error($"Print failed for {request.ImagePath}", ex);
            return new PrintResult(false, ex.Message, 0);
        }

        static bool imgIsLandscapeSafe(Image img) => img.Width > img.Height;
        static bool PageIsLandscape(PrintPageEventArgs e) => e.PageBounds.Width > e.PageBounds.Height;
    }
}
