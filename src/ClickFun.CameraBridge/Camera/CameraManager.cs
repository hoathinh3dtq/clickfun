namespace ClickFun.CameraBridge;

/// <summary>
/// Runtime-switchable camera facade. The bridge endpoints all talk to this wrapper;
/// the actual backend (fake | webcam | canon) can be swapped without restarting the
/// process, which is what the local admin's "Quản lý máy ảnh" page does.
/// </summary>
public sealed class CameraManager : ICameraService
{
    private readonly object _gate = new();
    private ICameraService _inner;
    private string _lastError = "";

    public CameraManager(ICameraService initial) => _inner = initial;

    public string Backend => Inner.Backend;
    public string LastError => _lastError;

    private ICameraService Inner => _inner;

    public Task<CameraStatus> ConnectAsync(CancellationToken ct = default) => Inner.ConnectAsync(ct);
    public Task DisconnectAsync() => Inner.DisconnectAsync();
    public Task<CameraStatus> GetStatusAsync() => Inner.GetStatusAsync();
    public (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame() => Inner.GetLatestFrame();
    public Task<CaptureResult> CaptureAsync(CancellationToken ct = default) => Inner.CaptureAsync(ct);
    public Task<bool> StartMovieAsync(CancellationToken ct = default) => Inner.StartMovieAsync(ct);
    public Task<bool> StopMovieAsync(CancellationToken ct = default) => Inner.StopMovieAsync(ct);
    public Task<CaptureResult> DownloadLastMovieAsync(CancellationToken ct = default) => Inner.DownloadLastMovieAsync(ct);

    public async Task SwitchAsync(string backend, int webcamDevice)
    {
        ICameraService next = backend.Trim().ToLowerInvariant() switch
        {
            "fake" => new FakeCameraService(),
            "webcam" => new WebcamCameraService(webcamDevice),
            "canon" or "canon-edsdk" or "helper" => new CanonEdsHelperService(),
            _ => throw new ArgumentException($"Unknown camera backend '{backend}'. Use fake | webcam | canon."),
        };

        ICameraService previous;
        lock (_gate)
        {
            previous = _inner;
            _inner = next;
        }

        _lastError = "";
        try { await previous.DisconnectAsync(); } catch (Exception ex) { BridgeLog.Warn($"switch: disconnect previous failed: {ex.Message}"); }
        try { previous.Dispose(); } catch { /* best effort */ }
        BridgeLog.Info($"Camera backend switched to {next.Backend}.");
    }

    public void Dispose()
    {
        lock (_gate) { _inner.Dispose(); }
    }
}
