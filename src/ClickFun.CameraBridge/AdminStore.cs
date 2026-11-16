using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClickFun.CameraBridge;

// Local admin data layer: owns ProgramData/ClickFun/admin/app-config-cache.json
// plus uploaded frame images in ProgramData/ClickFun/admin/frames/.
// The booth UI reads the same file via get_cached_app_config (Rust adapter).
public sealed class AdminStore
{
    private static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClickFun", "admin");

    public string RootDir => Root;
    public string CachePath => Path.Combine(Root, "app-config-cache.json");
    public string FramesDir => Path.Combine(Root, "frames");

    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public AdminStore()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(FramesDir);
        EnsureSeeded();
    }

    private void EnsureSeeded()
    {
        if (File.Exists(CachePath)) return;

        // Seed from the local HiHiBooth install's offline cache copy, if present.
        var legacyRoaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "com.hihi.boothv2", "offline", "app-config-cache.json");

        if (File.Exists(legacyRoaming))
        {
            try
            {
                var raw = File.ReadAllText(legacyRoaming);
                // Rewrite image paths from the original app dir to the ClickFun copy.
                raw = raw.Replace(@"com.hihi.boothv2\offline-assets", @"com.clickfun.booth\offline-assets");
                File.WriteAllText(CachePath, raw);
                BridgeLog.Info("Admin: seeded app-config-cache.json from legacy offline copy.");
                return;
            }
            catch (Exception ex)
            {
                BridgeLog.Warn($"Admin: seed failed ({ex.Message}); starting with an empty config.");
            }
        }

        File.WriteAllText(CachePath, JsonSerializer.Serialize(new
        {
            version = 1,
            paymentSettings = new { },
            layouts = Array.Empty<object>(),
            categories = Array.Empty<object>(),
            filters = Array.Empty<object>(),
            framesByShape = new Dictionary<string, object[]>(),
            updatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            lastSyncAtMs = (long?)null
        }, _json));
    }

    public JsonObject Load()
    {
        try
        {
            var raw = File.ReadAllText(CachePath);
            var node = JsonNode.Parse(raw) as JsonObject;
            if (node is not null) return node;
        }
        catch (Exception ex)
        {
            BridgeLog.Warn($"Admin: failed to read cache ({ex.Message}); returning empty config.");
        }
        return new JsonObject
        {
            ["version"] = 1,
            ["paymentSettings"] = new JsonObject(),
            ["layouts"] = new JsonArray(),
            ["categories"] = new JsonArray(),
            ["filters"] = new JsonArray(),
            ["framesByShape"] = new JsonObject(),
            ["updatedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    public void Save(JsonObject config)
    {
        config["updatedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tmp = CachePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, _json));
        File.Move(tmp, CachePath, overwrite: true);
    }

    public static long NextId(JsonArray items)
    {
        long max = 0;
        foreach (var item in items)
        {
            if (item is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<long>(out var id))
                if (id > max) max = id;
        }
        return max + 1;
    }

    public string SaveFrameImage(long id, string extension, Stream content)
    {
        var safeExt = extension.TrimStart('.').ToLowerInvariant();
        if (safeExt is not ("png" or "jpg" or "jpeg" or "webp"))
            throw new ArgumentException("only png/jpg/jpeg/webp are supported");
        var path = Path.Combine(FramesDir, $"{id}.{safeExt}");
        using var fs = File.Create(path);
        content.CopyTo(fs);
        return path;
    }

    public bool TryDeleteFrameFile(long id)
    {
        var deleted = false;
        foreach (var file in Directory.EnumerateFiles(FramesDir, $"{id}.*"))
        {
            try { File.Delete(file); deleted = true; } catch { /* best effort */ }
        }
        return deleted;
    }
}
