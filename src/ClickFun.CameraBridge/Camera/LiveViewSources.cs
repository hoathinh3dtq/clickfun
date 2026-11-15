using OpenCvSharp;

namespace ClickFun.CameraBridge;

/// <summary>
/// Live view source selection, mirroring the original booth:
///   auto   → HDMI/UVC capture card if one opens, else Canon EVF via EDSDK
///   hdmi   → force HDMI capture card (OpenCV / DirectShow device index)
///   edsdk  → force Canon EVF over USB (existing helper)
/// Capture is always full-res via USB EDSDK regardless of live view source.
/// </summary>
public interface ILiveViewSource : IDisposable
{
    string Kind { get; }
    bool Start();
    (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame();
}

public sealed class EdsdkLiveViewSource : ILiveViewSource
{
    private readonly ICameraService _camera;
    public EdsdkLiveViewSource(ICameraService camera) => _camera = camera;
    public string Kind => "edsdk";
    public bool Start() => true; // pump starts on camera connect
    public (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame() => _camera.GetLatestFrame();
    public void Dispose() { }
}

public sealed class HdmiLiveViewSource : ILiveViewSource
{
    public const string EnvDeviceIndex = "CLICKFUN_HDMI_DEVICE";

    private readonly object _gate = new();
    private VideoCapture? _capture;
    private Task? _pump;
    private CancellationTokenSource? _pumpCts;
    private (byte[] Jpeg, DateTime CapturedAtUtc)? _latest;
    private readonly int _deviceIndex;
    private readonly string _nameHint;

    public HdmiLiveViewSource(string nameHint = "auto")
    {
        _deviceIndex = int.TryParse(Environment.GetEnvironmentVariable(EnvDeviceIndex), out var i) ? i : 0;
        _nameHint = nameHint;
    }

    public string Kind => "hdmi";

    public bool Start()
    {
        Stop();
        for (var candidate = 0; candidate <= 3; candidate++)
        {
            var index = int.TryParse(Environment.GetEnvironmentVariable(EnvDeviceIndex), out var forced) ? forced : candidate;
            var cap = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (!cap.IsOpened()) { cap.Release(); continue; }

            var ok = false;
            using var frame = new Mat();
            for (var attempt = 0; attempt < 10 && !ok; attempt++)
                ok = cap.Read(frame) && !frame.Empty();

            if (!ok) { cap.Release(); continue; }

            _capture = cap;
            _pumpCts = new CancellationTokenSource();
            _pump = Task.Run(() => PumpLoop(_pumpCts.Token));
            BridgeLog.Info($"HDMI live view opened on device index {index}.");
            return true;
        }

        BridgeLog.Warn("HDMI live view: no UVC/capture-card device found (tried DSHOW indices 0-3).");
        return false;
    }

    public (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame()
    {
        lock (_gate) return _latest;
    }

    private async Task PumpLoop(CancellationToken ct)
    {
        var cap = _capture;
        if (cap is null) return;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
            using var frame = new Mat();
            if (!cap.Read(frame) || frame.Empty()) continue;

            if (!Cv2.ImEncode(".jpg", frame, out var jpg, new[] { (int)ImwriteFlags.JpegQuality, 80 }))
                continue;

            lock (_gate)
                _latest = (jpg, DateTime.UtcNow);
        }
    }

    public void Stop()
    {
        _pumpCts?.Cancel();
        try { _pump?.Wait(1000); } catch { /* pump stopping */ }
        _pumpCts = null;
        _pump = null;
        _capture?.Release();
        _capture = null;
    }

    public void Dispose() => Stop();
}

public static class LiveViewSelector
{
    /// <summary>Creates the preferred live view source: HDMI → EDSDK per env CLICKFUN_LIVEVIEW (auto|hdmi|edsdk).</summary>
    public static ILiveViewSource Select(ICameraService camera)
    {
        var pref = (Environment.GetEnvironmentVariable("CLICKFUN_LIVEVIEW") ?? "auto").Trim().ToLowerInvariant();
        if (pref == "edsdk") return new EdsdkLiveViewSource(camera);

        var hdmi = new HdmiLiveViewSource();
        if (hdmi.Start()) return hdmi;
        if (pref == "hdmi") return hdmi; // stay empty; caller reports no frames
        BridgeLog.Warn("Falling back to Canon EDSDK EVF live view (no HDMI device).");
        return new EdsdkLiveViewSource(camera);
    }
}
