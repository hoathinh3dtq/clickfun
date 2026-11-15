using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClickFun.CameraBridge;

/// <summary>
/// Canon EOS R camera service that talks to the Canon EDSDK through the user's
/// CanonEdsdkHelper process (JSON-lines over stdin/stdout), the same approach the
/// original booth used in production with an EOS R50. This avoids hosting EDSDK
/// (32-bit) inside our x64 bridge.
///
/// Helper resolution (first match wins):
///   1. CLICKFUN_CANON_HELPER  â†’ direct path to a prebuilt CanonEdsdkHelper.exe
///   2. CLICKFUN_EDSDK_DIR     â†’ folder containing EDSDK.dll (+ EdsImage.dll);
///      the bundled source tools/CanonEdsdkHelper.cs is compiled on demand
///      with the inbox csc.exe (x86) into %TEMP%\clickfun-canon\
///
/// Protocol (per line):
///   â†’ {"id":N,"cmd":"status"|"liveViewStart"|...}
///   â† {"id":N,"ok":true,"result":{...}}   or   {"id":N,"ok":false,"code":"...","eds":N,"message":"..."}
///   â† {"event":"ready"|"disconnected"|"fatal", ...}   (unsolicited)
/// </summary>
public sealed class CanonEdsHelperService : ICameraService, IAsyncDisposable
{
    public string Backend => "canon-edsdk-helper";

    private readonly object _stdinLock = new();
    private Process? _helper;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private int _nextId;
    private readonly SemaphoreSlim _frameLock = new(1, 1);
    private byte[]? _latestFrame;
    private DateTime _latestFrameAtUtc = DateTime.MinValue;
    private bool _liveViewStarted;
    private long _framePumpGeneration;
    private string _cameraModel = "Canon EOS R";
    private string? _serial;
    private long? _battery;
    private bool _connected;

    public async Task<CameraStatus> ConnectAsync(CancellationToken ct = default)
    {
        await EnsureHelperAsync().ConfigureAwait(false);
        if (_helper is null || _helper.HasExited)
            throw new InvalidOperationException("Canon EDSDK helper failed to start â€” check logs and CLICKFUN_EDSDK_DIR/CLICKFUN_CANON_HELPER.");

        _connected = true;
        StartFramePump();
        return await GetStatusAsync().ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        _connected = false;
        Interlocked.Increment(ref _framePumpGeneration);
        _latestFrame = null;
        if (_helper is { HasExited: false })
        {
            try { await SendCommandAsync("close", null, TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception ex) { BridgeLog.Warn($"helper close command failed: {ex.Message}"); }
            try { _helper.Kill(); } catch { /* already gone */ }
        }
        _helper = null;
    }

    public async Task<CameraStatus> GetStatusAsync()
    {
        try
        {
            var result = await SendCommandAsync("status", null, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var connected = result?["connected"]?.GetValue<bool>() ?? false;
            _connected = connected;
            _cameraModel = result?["model"]?.GetValue<string>() ?? _cameraModel;
            _serial = result?["serial"]?.GetValue<string>();
            _battery = result?["battery"]?.GetValue<long>();
            return new CameraStatus(connected, _cameraModel, _battery is null ? null : $"{_battery}", null, Backend);
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"status roundtrip failed: {ex.Message}");
            return new CameraStatus(false, _cameraModel, null, null, Backend);
        }
    }

    public (byte[] Jpeg, DateTime CapturedAtUtc)? GetLatestFrame()
    {
        var frame = _latestFrame;
        return frame is null ? null : (frame, _latestFrameAtUtc);
    }

    public async Task<CaptureResult> CaptureAsync(CancellationToken ct = default)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ClickFun", "photos", DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"CLICKFUN_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");

        try
        {
            var result = await SendCommandAsync("capture", new JsonObject
            {
                ["path"] = path,
                ["autofocus"] = true,
                ["timeoutMs"] = 20000,
            }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            var actualPath = result?["path"]?.GetValue<string>() ?? path;
            return new CaptureResult(true, actualPath, null);
        }
        catch (Exception ex)
        {
            BridgeLog.Error("capture failed", ex);
            return new CaptureResult(false, null, ex.Message);
        }
    }

    public Task<bool> StartMovieAsync(CancellationToken ct = default) =>
        Task.FromResult(false); // EDSDK movie record is not implemented in the helper (photo-only MVP)

    public Task<bool> StopMovieAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<CaptureResult> DownloadLastMovieAsync(CancellationToken ct = default) =>
        Task.FromResult(new CaptureResult(false, null, "movie recording is not wired yet â€” the Canon helper exposes photo capture only"));

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _frameLock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- internals ----------

    private async Task EnsureHelperAsync()
    {
        if (_helper is { HasExited: false }) return;

        var (exe, cwd) = await ResolveHelperAsync().ConfigureAwait(false);
        _helper = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        _helper.OutputDataReceived += (_, e) => OnHelperLine(e.Data);
        _helper.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) BridgeLog.Warn($"helper stderr: {e.Data}"); };
        _helper.Exited += (_, e) =>
        {
            BridgeLog.Warn($"helper exited (code {e}).");
            _connected = false;
            FailAllPending(new InvalidOperationException("helper process exited"));
        };

        _helper.Start();
        _helper.BeginOutputReadLine();
        _helper.BeginErrorReadLine();
        BridgeLog.Info($"Canon EDSDK helper started: {exe}");

        // Wait for the helper's "ready" event (or proceed after a short grace period).
        await Task.Delay(800).ConfigureAwait(false);
    }

    private static async Task<(string Exe, string Cwd)> ResolveHelperAsync()
    {
        var direct = Environment.GetEnvironmentVariable("CLICKFUN_CANON_HELPER");
        if (!string.IsNullOrWhiteSpace(direct) && File.Exists(direct))
            return (direct, Path.GetDirectoryName(direct)!);

        var edsdkDir = Environment.GetEnvironmentVariable("CLICKFUN_EDSDK_DIR");
        var scratch = Path.Combine(Path.GetTempPath(), "clickfun-canon");
        Directory.CreateDirectory(scratch);

        if (!string.IsNullOrWhiteSpace(edsdkDir) && File.Exists(Path.Combine(edsdkDir, "EDSDK.dll")))
        {
            File.Copy(Path.Combine(edsdkDir, "EDSDK.dll"), Path.Combine(scratch, "EDSDK.dll"), overwrite: true);
            var edsImage = Path.Combine(edsdkDir, "EdsImage.dll");
            if (File.Exists(edsImage))
                File.Copy(edsImage, Path.Combine(scratch, "EdsImage.dll"), overwrite: true);

            var source = FindBundledHelperSource()
                ?? throw new FileNotFoundException("CanonEdsdkHelper.cs not found next to the bridge or in the repository tools/ folder.");
            var csPath = Path.Combine(scratch, "CanonEdsdkHelper.cs");
            File.Copy(source, csPath, overwrite: true);
            var exe = Path.Combine(scratch, "CanonEdsdkHelper.exe");

            var windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
            var csc = Path.Combine(windir, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe");
            if (!File.Exists(csc))
                throw new FileNotFoundException($"csc.exe not found at {csc} â€” cannot build CanonEdsdkHelper (needs .NET Framework 4.x, inbox on Windows).");

            var psi = new ProcessStartInfo(csc,
                $"/nologo /target:exe /platform:x86 /optimize+ /r:System.Web.Extensions.dll /r:System.Drawing.dll /out:\"{exe}\" \"{csPath}\"")
            {
                WorkingDirectory = scratch,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var build = Process.Start(psi)!;
            await build.WaitForExitAsync().ConfigureAwait(false);
            if (build.ExitCode != 0)
            {
                var err = await build.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                throw new InvalidOperationException("CanonEdsdkHelper compile failed: " + err.Trim());
            }
            BridgeLog.Info("CanonEdsdkHelper compiled from bundled source.");
            return (exe, scratch);
        }

        // Production layout: helper exes ship next to the bridge under canon-edsdk/
        var localDir = Path.Combine(AppContext.BaseDirectory, "canon-edsdk");
        var localExe = Path.Combine(localDir, "CanonEdsdkHelper.exe");
        if (File.Exists(localExe))
            return (localExe, localDir);

        throw new FileNotFoundException(
            "Canon EDSDK not configured. Set CLICKFUN_CANON_HELPER=<path to CanonEdsdkHelper.exe> " +
            "or CLICKFUN_EDSDK_DIR=<folder containing EDSDK.dll + EdsImage.dll>. " +
            "Your Canon EOS SDK binaries stay on your machine and are never committed.");
    }

    private static string? FindBundledHelperSource()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "CanonEdsdkHelper.cs"),
            Path.Combine(AppContext.BaseDirectory, "CanonEdsdkHelper.cs"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "CanonEdsdkHelper.cs")),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private void OnHelperLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        JsonNode? node;
        try { node = JsonNode.Parse(line); }
        catch (Exception ex) { BridgeLog.Warn($"unparseable helper line: {ex.Message}"); return; }
        if (node is not JsonObject obj) return;

        if (obj["event"] is JsonValue ev && ev.GetValue<string>() == "disconnected")
        {
            BridgeLog.Warn("helper reports camera disconnected.");
            _connected = false;
            _liveViewStarted = false;
            return;
        }
        if (obj["event"] is JsonValue fatal && fatal.GetValue<string>() == "fatal")
        {
            BridgeLog.Error($"helper fatal: {obj["message"]?.GetValue<string>()}");
            return;
        }

        if (obj["id"] is JsonValue idValue && idValue.TryGetValue<int>(out var id) &&
            _pending.TryRemove(id, out var tcs))
        {
            tcs.TrySetResult(obj);
        }
    }

    private async Task<JsonObject?> SendCommandAsync(string cmd, JsonObject? parameters, TimeSpan timeout)
    {
        var helper = _helper ?? throw new InvalidOperationException("helper not running");
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var payload = new JsonObject { ["id"] = id, ["cmd"] = cmd };
        if (parameters is not null)
            foreach (var kvp in parameters)
                payload[kvp.Key] = kvp.Value?.DeepClone();

        var line = payload.ToJsonString();
        lock (_stdinLock)
        {
            helper.StandardInput.WriteLine(line);
            helper.StandardInput.Flush();
        }

        using var cts = new CancellationTokenSource(timeout);
        await using var reg = cts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var expired))
                expired.TrySetException(new TimeoutException($"helper command '{cmd}' timed out after {timeout.TotalSeconds}s"));
        }).ConfigureAwait(false);

        var response = await tcs.Task.ConfigureAwait(false);
        var ok = response["ok"]?.GetValue<bool>() ?? false;
        if (!ok)
        {
            var code = response["code"]?.GetValue<string>() ?? "UNKNOWN";
            var message = response["message"]?.GetValue<string>() ?? "helper command failed";
            var eds = response["eds"]?.GetValue<long>();
            throw new HelperCommandException(code, eds, message);
        }
        return response["result"] as JsonObject;
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var (_, tcs) in _pending)
            tcs.TrySetException(ex);
        _pending.Clear();
    }

    private void StartFramePump()
    {
        var generation = Interlocked.Increment(ref _framePumpGeneration);
        _ = Task.Run(async () =>
        {
            try
            {
                if (!_liveViewStarted)
                {
                    try { await SendCommandAsync("liveViewStart", null, TimeSpan.FromSeconds(5)); _liveViewStarted = true; }
                    catch (Exception ex) { BridgeLog.Error("liveViewStart failed â€” live view disabled", ex); }
                }

                while (generation == Volatile.Read(ref _framePumpGeneration) && _connected)
                {
                    await Task.Delay(400).ConfigureAwait(false);
                    try
                    {
                        var result = await SendCommandAsync("liveViewFrame", null, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        var b64 = result?["jpeg"]?.GetValue<string>();
                        if (b64 is not null)
                        {
                            var bytes = Convert.FromBase64String(b64);
                            // crude single-slot cache; UI polls ~4 Hz anyway
                            if (await _frameLock.WaitAsync(0))
                            {
                                try { _latestFrame = bytes; _latestFrameAtUtc = DateTime.UtcNow; }
                                finally { _frameLock.Release(); }
                            }
                        }
                    }
                    catch (HelperCommandException ex) when (ex.Code == "NOT_READY" || ex.Message.Contains("notReady", StringComparison.OrdinalIgnoreCase))
                    {
                        // camera still warming up â€” skip this tick
                    }
                    catch (Exception ex) when (generation == Volatile.Read(ref _framePumpGeneration))
                    {
                        BridgeLog.Warn($"liveViewFrame pump error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Error("frame pump crashed", ex);
            }
        });
    }
}

public sealed class HelperCommandException : Exception
{
    public string Code { get; }
    public long? EdsError { get; }

    public HelperCommandException(string code, long? edsError, string message) : base(message)
    {
        Code = code;
        EdsError = edsError;
    }
}

