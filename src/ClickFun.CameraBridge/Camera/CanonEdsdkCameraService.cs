namespace ClickFun.CameraBridge;

/// <summary>
/// Canon EDSDK-backed camera service for Canon EOS R series.
///
/// STATUS: wired to the environment and structured, but the actual EDSDK P/Invoke
/// calls are intentionally left as a focused TODO because the exact wrapper you own
/// (your "Canon EDSDK sample/code") was not provided yet. The interface and the IPC
/// contract are final; only the adapter internals are pending.
///
/// Integration plan (once your sample code is supplied):
///   1. Drop your wrapper (or direct P/Invoke decls) into this folder.
///   2. Set CANON_EDSDK_PATH to the folder containing EDSDK.dll (never committed).
///   3. Implement: InitializeSDK → OpenSession → EnableEVF (OutputDevice=LCD) →
///      EVF frame pump → CaptureImage/Download → MovieStart/MovieStop.
///   4. Map Canon property IDs for the exact R model (model via CLICKFUN_CAMERA_MODEL).
/// </summary>
public sealed class CanonEdsdkCameraService : ICameraService
{
    public string Backend => "canon-edsdk";

    private readonly string _edsdkPath;
    private bool _connected;

    public CanonEdsdkCameraService()
    {
        _edsdkPath = Environment.GetEnvironmentVariable("CANON_EDSDK_PATH") ?? "";
    }

    public Task<CameraStatus> ConnectAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_edsdkPath))
            throw new InvalidOperationException(
                "CANON_EDSDK_PATH is not set. Point it at the folder containing EDSDK.dll " +
                "(your Canon EOS SDK install), or run with CLICKFUN_CAMERA=fake.");

        // TODO(canon-edsdk): EdsInitializeSDK → EdsGetCameraList → EdsOpenSession →
        //   EdsSetPropertyData(kEdsPropID_Evf_OutputDevice, kEdsEvfOutputDevice_PC)
        //   then start the EVF download pump on a dedicated thread.
        BridgeLog.Warn("CanonEdsdkCameraService.ConnectAsync called but EDSDK adapter is not implemented yet. " +
                       "Supply your sample code and it will be wired here.");
        _connected = false;
        return Task.FromResult(new CameraStatus(false, "Canon EOS R (not wired)", null, null, Backend));
    }

    public Task DisconnectAsync() => Task.CompletedTask;
    public Task<CameraStatus> GetStatusAsync() =>
        Task.FromResult(new CameraStatus(_connected, "Canon EOS R", null, null, Backend));
    public (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame() => null;

    public Task<CaptureResult> CaptureAsync(CancellationToken ct = default) =>
        Task.FromResult(new CaptureResult(false, null, "Canon EDSDK adapter not implemented — see Canon/CanonEdsdkCameraService.cs"));

    public Task<bool> StartMovieAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> StopMovieAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<CaptureResult> DownloadLastMovieAsync(CancellationToken ct = default) =>
        Task.FromResult(new CaptureResult(false, null, "Canon EDSDK adapter not implemented"));

    public void Dispose() { }
}
