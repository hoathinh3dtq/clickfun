using OpenCvSharp;

namespace ClickFun.CameraBridge;

/// <summary>
/// Abstraction over the camera. Implementations: FakeCameraService (development /
/// end-to-end testing without hardware) and CanonEdsdkCameraService (production,
/// requires the Canon EDSDK). Canon EOS R live view arrives as EVF frames
/// (~1024px JPEG) and captures as full-res files on the card.
/// </summary>
public interface ICameraService : IDisposable
{
    string Backend { get; }
    Task<CameraStatus> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task<CameraStatus> GetStatusAsync();

    /// <summary>Latest frame from live view, or null if not streaming. Thread-safe.</summary>
    (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame();

    /// <summary>Triggers a full-resolution capture and downloads it. Returns the file path.</summary>
    Task<CaptureResult> CaptureAsync(CancellationToken ct = default);

    /// <summary>Starts / stops on-camera movie recording (EOS R series).</summary>
    Task<bool> StartMovieAsync(CancellationToken ct = default);
    Task<bool> StopMovieAsync(CancellationToken ct = default);
    Task<CaptureResult> DownloadLastMovieAsync(CancellationToken ct = default);
}

/// <summary>
/// Synthetic camera for development. Generates a slowly-shifting gradient frame with
/// an embedded timestamp and the current frame counter, so the UI/preview/overlay
/// pipeline can be exercised end-to-end without hardware.
/// </summary>
public sealed class FakeCameraService : ICameraService
{
    public string Backend => "fake";

    private bool _connected;
    private long _frameCount;
    private DateTime _startedUtc = DateTime.UtcNow;

    public Task<CameraStatus> ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        _startedUtc = DateTime.UtcNow;
        BridgeLog.Info("FakeCamera connected (synthetic frames).");
        return Task.FromResult(GetStatus());
    }

    public Task DisconnectAsync()
    {
        _connected = false;
        BridgeLog.Info("FakeCamera disconnected.");
        return Task.CompletedTask;
    }

    public Task<CameraStatus> GetStatusAsync() => Task.FromResult(GetStatus());

    public (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame()
    {
        if (!_connected) return null;

        var n = Interlocked.Increment(ref _frameCount);
        var seconds = (DateTime.UtcNow - _startedUtc).TotalSeconds;
        var hueShift = (int)(seconds * 8) % 180;

        using var img = new Mat(480, 640, MatType.CV_8UC3, new Scalar(30, 30, 40));
        // Moving gradient square as "subject"
        var x = 60 + (int)((Math.Sin(seconds * 0.9) * 0.5 + 0.5) * 380);
        var y = 140 + (int)((Math.Cos(seconds * 0.7) * 0.5 + 0.5) * 160);
        Cv2.Rectangle(img, new Rect(x, y, 110, 110), new Scalar(60 + hueShift, 180, 240), thickness: -1);
        Cv2.PutText(img, "CLICKFUN FAKE CAM", new OpenCvSharp.Point(24, 40), HersheyFonts.HersheySimplex, 0.9, new Scalar(235, 235, 235), 2);
        Cv2.PutText(img, $"{DateTime.Now:HH:mm:ss.fff}   frame {n}", new OpenCvSharp.Point(24, 450), HersheyFonts.HersheySimplex, 0.7, new Scalar(180, 220, 255), 1);

        Cv2.ImEncode(".jpg", img, out var jpg, new[] { (int)ImwriteFlags.JpegQuality, 80 });
        return (jpg, DateTime.UtcNow);
    }

    public async Task<CaptureResult> CaptureAsync(CancellationToken ct = default)
    {
        if (!_connected) return new CaptureResult(false, null, "camera not connected");
        await Task.Delay(300, ct); // simulate mirror-less shutter latency

        var frame = GetLatestFrame();
        if (frame is null) return new CaptureResult(false, null, "no frame available");

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ClickFun", "photos", DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"CLICKFUN_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}.jpg");
        await File.WriteAllBytesAsync(path, frame.Value.Jpeg, ct);
        BridgeLog.Info($"FakeCamera captured -> {path}");
        return new CaptureResult(true, path, null);
    }

    public Task<bool> StartMovieAsync(CancellationToken ct = default) => Task.FromResult(_connected);
    public Task<bool> StopMovieAsync(CancellationToken ct = default) => Task.FromResult(_connected);
    public Task<CaptureResult> DownloadLastMovieAsync(CancellationToken ct = default) =>
        Task.FromResult(new CaptureResult(false, null, "fake camera has no movie file; wire Canon EDSDK to enable"));

    public void Dispose() { }

    private CameraStatus GetStatus() => new(_connected, "Fake Camera 640x480", "100% (fake)", 10_000_000_000, Backend);
}
