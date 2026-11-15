using System.Text.Json;

namespace ClickFun.CameraBridge;

public sealed class SettingsStore
{
    private static readonly string ProgramData =
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public string RootDir { get; }
    public string SettingsPath { get; }
    public string PhotosRoot { get; private set; }
    public string LogsDir { get; }

    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public SettingsStore()
    {
        RootDir = Path.Combine(ProgramData, "ClickFun");
        SettingsPath = Path.Combine(RootDir, "settings.json");
        PhotosRoot = Path.Combine(RootDir, "photos");
        LogsDir = Path.Combine(RootDir, "logs");
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(PhotosRoot);
        Directory.CreateDirectory(LogsDir);
    }

    public SettingsDto Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var dto = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(SettingsPath), _json);
                if (dto is not null) return dto;
            }
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Failed to read settings at {SettingsPath}: {ex.Message}. Falling back to defaults.");
        }
        return Defaults();
    }

    public SettingsDto Save(SettingsDto dto)
    {
        var json = JsonSerializer.Serialize(dto, _json);
        File.WriteAllText(SettingsPath, json);
        return dto;
    }

    public static SettingsDto Defaults() => new(
        PrinterName: "",            // empty = system default
        Copies: 1,
        Zoom: 0.98,
        OffsetX: 0,
        OffsetY: 0,
        CountdownSeconds: 3,
        DefaultFilter: "none",
        OverlayTemplate: "",
        PhotosRoot: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClickFun", "photos"),
        VideoMaxSeconds: 10,
        PaperLayout: "auto");
}

public static class BridgeLog
{
    private static readonly object Gate = new();
    private static string? _logFile;

    public static void Init(string logsDir)
    {
        Directory.CreateDirectory(logsDir);
        _logFile = Path.Combine(logsDir, $"bridge-{DateTime.Now:yyyyMMdd}.log");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Console.WriteLine(line);
        lock (Gate)
        {
            try { if (_logFile is not null) File.AppendAllText(_logFile, line + Environment.NewLine); }
            catch { /* never throw from logging */ }
        }
    }
}
