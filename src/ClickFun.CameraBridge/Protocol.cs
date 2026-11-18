namespace ClickFun.CameraBridge;

public sealed record PrintRequest(string ImagePath, int Copies = 1, double? Zoom = null,
    int? OffsetX = null, int? OffsetY = null, string? PrinterName = null, string? PaperLayout = null);

public sealed record PrintResult(bool Success, string Message, int CopiesPrinted, string? PaperLayout);

public sealed record CaptureResult(bool Success, string? FilePath, string? Error);

public sealed record CameraStatus(bool Connected, string CameraModel, string? Battery, long? FreeBytesOnCard, string Backend);

public sealed record SettingsDto(
    string PrinterName,
    int Copies,
    double Zoom,
    int OffsetX,
    int OffsetY,
    int CountdownSeconds,
    string DefaultFilter,
    string OverlayTemplate,
    string PhotosRoot,
    int VideoMaxSeconds,
    string PaperLayout = "auto",
    string CameraBackend = "fake",
    int WebcamDevice = 0,
    string LiveView = "auto");

public sealed record HealthDto(string Status, string Version, string CameraBackend, bool PrinterAvailable);
