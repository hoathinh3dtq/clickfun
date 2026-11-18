using OpenCvSharp;

namespace ClickFun.CameraBridge;

/// <summary>
/// UVC webcam / capture card as a camera source. Opens a DirectShow device by index,
/// streams frames for preview, and captures a full-res frame for the booth flow.
/// Not as good as Canon EDSDK for photo quality, but it makes this machine fully
/// operational without the Canon attached (live view + shutter work end-to-end).
/// </summary>
public sealed class WebcamCameraService : ICameraService
{
    private readonly int _deviceIndex;
    private VideoCapture? _capture;
    private readonly object _gate = new();
    private bool _connected;
    private string _model = "Webcam / Capture Card";

    public WebcamCameraService(int deviceIndex = 0)
    {
        _deviceIndex = deviceIndex;
    }

    public string Backend => "webcam";

    public Task<CameraStatus> ConnectAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            ReleaseInner();
            _capture = new VideoCapture(_deviceIndex, VideoCaptureAPIs.DSHOW);
            if (!_capture.IsOpened())
            {
                _capture = null;
                return Task.FromResult(new CameraStatus(false, _model, null, null, Backend));
            }

            var w = _capture.Get(VideoCaptureProperties.FrameWidth);
            var h = _capture.Get(VideoCaptureProperties.FrameHeight);
            if (w > 0 && h > 0) _model = $"Webcam (index {_deviceIndex}, {(int)w}×{(int)h})";
            else _model = $"Webcam (index {_deviceIndex})";
            _connected = true;
            BridgeLog.Info($"Webcam connected: {_model}");
            return Task.FromResult(GetStatus());
        }
    }

    public Task DisconnectAsync()
    {
        lock (_gate) { _connected = false; ReleaseInner(); }
        BridgeLog.Info("Webcam disconnected.");
        return Task.CompletedTask;
    }

    public Task<CameraStatus> GetStatusAsync() => Task.FromResult(GetStatus());

    public (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame()
    {
        lock (_gate)
        {
            if (!_connected || _capture is null || !_capture.IsOpened()) return null;
            using var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty()) return null;
            Cv2.ImEncode(".jpg", frame, out var jpg, new[] { (int)ImwriteFlags.JpegQuality, 80 });
            return (jpg, DateTime.UtcNow);
        }
    }

    public async Task<CaptureResult> CaptureAsync(CancellationToken ct = default)
    {
        if (!_connected) return new CaptureResult(false, null, "camera not connected");

        (byte[] Jpeg, DateTime CapturedAtUtc)? frame = null;
        for (var attempt = 0; attempt < 5 && frame is null; attempt++)
        {
            frame = GetLatestFrame();
            if (frame is null) await Task.Delay(150, ct).ConfigureAwait(false);
        }

        if (frame is null) return new CaptureResult(false, null, "no frame from webcam");

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ClickFun", "photos", DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"CLICKFUN_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}.jpg");
        await File.WriteAllBytesAsync(path, frame.Value.Jpeg, ct);
        BridgeLog.Info($"Webcam captured -> {path}");
        return new CaptureResult(true, path, null);
    }

    public Task<bool> StartMovieAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> StopMovieAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task<CaptureResult> DownloadLastMovieAsync(CancellationToken ct = default) =>
        Task.FromResult(new CaptureResult(false, null, "webcam capture does not record movies"));

    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();

    private void ReleaseInner()
    {
        try { _capture?.Release(); } catch { /* ignored */ }
        _capture?.Dispose();
        _capture = null;
    }

    private CameraStatus GetStatus() =>
        new(_connected, _model, null, null, Backend);
}
