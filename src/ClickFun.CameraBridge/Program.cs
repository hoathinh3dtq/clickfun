using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using ClickFun.CameraBridge;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

var settings = new SettingsStore();
BridgeLog.Init(settings.LogsDir);
BridgeLog.Info("ClickFun.CameraBridge starting…");

var cameraBackend = (Environment.GetEnvironmentVariable("CLICKFUN_CAMERA") ?? "fake").Trim().ToLowerInvariant();
ICameraService camera = cameraBackend switch
{
    "canon" or "canon-edsdk" or "helper" => new CanonEdsHelperService(),
    _ => new FakeCameraService(),
};
BridgeLog.Info($"Camera backend: {camera.Backend}");

var token = Environment.GetEnvironmentVariable("CLICKFUN_BRIDGE_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    token = "clickfun-dev-token";
    BridgeLog.Warn("CLICKFUN_BRIDGE_TOKEN not set — using development token. Set a strong token for kiosk deployment.");
}

var port = int.TryParse(Environment.GetEnvironmentVariable("CLICKFUN_BRIDGE_PORT"), out var p) ? p : 8737;
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

var app = builder.Build();

// Token gate. MJPEG/frame endpoints also accept ?token= for <img> tags.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path == "/health") { await next(); return; }

    // Admin static assets are served without a token; /admin/* API endpoints stay token-gated.
    var p = ctx.Request.Path.Value ?? string.Empty;
    var isAdminShell = p == "/admin" || p == "/admin/";
    var isAdminStatic = (p.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase) &&
        (new[] { ".js", ".css", ".png", ".jpg", ".jpeg", ".webp", ".svg", ".ico", ".woff", ".woff2", ".ttf", ".json", ".map" }
            .Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)))
        || (p.StartsWith("/admin/framefile/", StringComparison.OrdinalIgnoreCase));
    if (isAdminShell || isAdminStatic) { await next(); return; }

    var isMedia = ctx.Request.Path == "/stream.mjpg" || ctx.Request.Path == "/frame.jpg" || ctx.Request.Path == "/frame/latest";;
    var headerOk = ctx.Request.Headers.TryGetValue("X-ClickFun-Token", out var h) && h == token;
    var queryOk = isMedia && ctx.Request.Query.TryGetValue("token", out var q) && q == token;

    if (!headerOk && !queryOk)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "missing or invalid X-ClickFun-Token" });
        return;
    }
    await next();
});

var liveView = LiveViewSelector.Select(camera);
BridgeLog.Info($"Live view source: {liveView.Kind} (CLICKFUN_LIVEVIEW={(Environment.GetEnvironmentVariable("CLICKFUN_LIVEVIEW") ?? "auto")})");

// ---------- MJPEG live view (same contract as the original booth bridge) ----------

app.MapGet("/stream.mjpg", async (HttpContext ctx) =>
{
    ctx.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
    ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    ctx.Response.Headers.Pragma = "no-cache";
    ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
    ctx.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
    while (!cts.IsCancellationRequested)
    {
        var frame = liveView.GetLatestFrame();
        if (frame is not null)
        {
            var (jpeg, _) = frame.Value;
            await ctx.Response.WriteAsync($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n", cts.Token);
            await ctx.Response.Body.WriteAsync(jpeg, cts.Token);
            await ctx.Response.WriteAsync("\r\n", cts.Token);
            await ctx.Response.Body.FlushAsync(cts.Token);
        }
        await Task.Delay(400, cts.Token).ConfigureAwait(false);
    }
});

app.MapGet("/frame.jpg", () =>
{
    var frame = liveView.GetLatestFrame();
    if (frame is null) return Results.NoContent();
    return Results.Bytes(frame.Value.Jpeg, "image/jpeg");
});

app.MapGet("/liveview/mode", () => Results.Ok(new { mode = liveView.Kind }));

app.MapGet("/frame/latest", () =>
{
    var frame = liveView.GetLatestFrame();
    if (frame is null) return Results.NoContent();
    return Results.Bytes(frame.Value.Jpeg, "image/jpeg");
});

// ---------- Camera ----------

app.MapGet("/health", () =>
{
    var s = settings.Load();
    return Results.Ok(new HealthDto("ok", "0.1.0", camera.Backend,
        PrintService.IsPrinterAvailable(s.PrinterName)));
});

app.MapGet("/camera/status", async () => Results.Ok(await camera.GetStatusAsync()));

app.MapPost("/camera/connect", async () =>
{
    try { return Results.Ok(await camera.ConnectAsync()); }
    catch (Exception ex) { BridgeLog.Error("connect failed", ex); return Results.Problem(ex.Message, statusCode: 500); }
});

app.MapPost("/camera/disconnect", async () =>
{
    await camera.DisconnectAsync();
    return Results.Ok(new { disconnected = true });
});

app.MapPost("/capture", async () =>
{
    var result = await camera.CaptureAsync();
    return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: 502);
});

app.MapPost("/preview", async (HttpRequest req) =>
{
    var body = await req.ReadFromJsonAsync<PreviewRequest>();
    if (body is null || string.IsNullOrWhiteSpace(body.ImagePath) || !File.Exists(body.ImagePath))
        return Results.BadRequest(new { error = "imagePath missing or not found" });

    var bytes = await File.ReadAllBytesAsync(body.ImagePath);
    bytes = FilterEngine.Apply(bytes, body.Filter ?? settings.Load().DefaultFilter);
    bytes = FilterEngine.ApplyOverlay(bytes, body.OverlayTemplate ?? settings.Load().OverlayTemplate);
    return Results.Bytes(bytes, "image/jpeg");
});

app.MapPost("/video/start", async () => Results.Ok(new { started = await camera.StartMovieAsync() }));
app.MapPost("/video/stop", async () => Results.Ok(new { stopped = await camera.StopMovieAsync() }));

app.MapPost("/video/download", async () =>
{
    var result = await camera.DownloadLastMovieAsync();
    return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: 502);
});

// ---------- Settings & print ----------

app.MapGet("/settings", () => Results.Ok(settings.Load()));

app.MapPut("/settings", async (HttpRequest req) =>
{
    var dto = await req.ReadFromJsonAsync<SettingsDto>();
    if (dto is null) return Results.BadRequest(new { error = "invalid settings payload" });
    return Results.Ok(settings.Save(dto));
});

app.MapGet("/print/layouts", () =>
{
    var s = settings.Load();
    var printer = string.IsNullOrWhiteSpace(s.PrinterName) ? null : s.PrinterName;
    var detected = printer is null
        ? PrintService.DetectLayouts(PrinterSettingsInstalledFirst())
        : PrintService.DetectLayouts(printer);
    return Results.Ok(new { printer = printer ?? "(default)", layouts = detected });

    static string? PrinterSettingsInstalledFirst() =>
        System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().FirstOrDefault();
});

app.MapPost("/print", async (HttpRequest req) =>
{
    var request = await req.ReadFromJsonAsync<PrintRequest>();
    if (request is null) return Results.BadRequest(new { error = "invalid print request" });
    var printer = new PrintService(settings);
    var result = printer.Print(request);
    return result.Success ? Results.Ok(result) : Results.Problem(result.Message, statusCode: 502);
});

// ---------- Local admin API (own data layer, served at /admin) ----------

var admin = new AdminStore();

static JsonArray FlattenFrames(JsonObject cfg)
{
    var all = new JsonArray();
    if (cfg["framesByShape"] is JsonObject byShape)
    {
        foreach (var (shape, arr) in byShape)
        {
            if (arr is not JsonArray a) continue;
            foreach (var f in a)
            {
                if (f is not JsonObject o) continue;
                var clone = (JsonObject)o.DeepClone();
                if (clone["frame"] is null) clone["frame"] = shape;
                all.Add(clone);
            }
        }
    }
    return all;
}

static JsonObject? FindFrame(JsonObject cfg, long id)
{
    if (cfg["framesByShape"] is not JsonObject byShape) return null;
    foreach (var (_, arr) in byShape)
    {
        if (arr is not JsonArray a) continue;
        foreach (var f in a)
            if (f is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<long>(out var fid) && fid == id)
                return o;
    }
    return null;
}

static void RemoveFrame(JsonObject cfg, long id)
{
    if (cfg["framesByShape"] is not JsonObject byShape) return;
    foreach (var (shape, arr) in byShape)
    {
        if (arr is not JsonArray a) continue;
        for (var i = a.Count - 1; i >= 0; i--)
        {
            if (a[i] is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<long>(out var fid) && fid == id)
                a.RemoveAt(i);
        }
    }
}

static JsonArray FramesOfShape(JsonObject cfg, string shape)
{
    if (cfg["framesByShape"] is JsonObject byShape && byShape[shape] is JsonArray a)
        return a;
    var created = new JsonArray();
    if (cfg["framesByShape"] is JsonObject bo) bo[shape] = created;
    return created;
}

app.MapGet("/admin/config", () => Results.Ok(admin.Load()));

app.MapGet("/admin/stats", () =>
{
    var cfg = admin.Load();
    var frames = FlattenFrames(cfg).Count;
    var layouts = (cfg["layouts"] as JsonArray)?.Count ?? 0;
    var categories = (cfg["categories"] as JsonArray)?.Count ?? 0;
    var filters = (cfg["filters"] as JsonArray)?.Count ?? 0;
    return Results.Ok(new
    {
        frames,
        layouts,
        categories,
        filters,
        updatedAtMs = cfg["updatedAtMs"],
        lastSyncAtMs = cfg["lastSyncAtMs"],
        cameraBackend = camera.Backend,
        printerAvailable = PrintService.IsPrinterAvailable(settings.Load().PrinterName),
        version = "0.1.0"
    });
});

app.MapGet("/admin/frames", () =>
{
    var cfg = admin.Load();
    return Results.Ok(FlattenFrames(cfg));
});

app.MapPost("/admin/frames", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "expected multipart/form-data" });
    var form = await req.ReadFormAsync();

    var name = form["name"].ToString().Trim();
    var shape = form["shape"].ToString().Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "name required" });
    if (shape is not ("square" or "grid" or "strip")) return Results.BadRequest(new { error = "shape must be square|grid|strip" });

    var file = form.Files["file"];
    if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file required" });

    using var content = file.OpenReadStream();
    var ext = Path.GetExtension(file.FileName);
    long newId;
    string savedPath;
    try
    {
        var cfgForId = admin.Load();
        var flat = FlattenFrames(cfgForId);
        newId = AdminStore.NextId(flat);
        savedPath = admin.SaveFrameImage(newId, ext, content);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }

    var cfg = admin.Load();
    var frame = new JsonObject
    {
        ["id"] = newId,
        ["name"] = name,
        ["frame"] = shape,
        ["type"] = "capture",
        ["is_active"] = true,
        ["enable_overlay"] = false,
        ["filename"] = Path.GetFileName(savedPath),
        ["path"] = savedPath,
        ["url"] = savedPath,
        ["category_id"] = long.TryParse(form["category_id"].ToString(), out var cid) ? cid : 0,
        ["uuid"] = Guid.NewGuid().ToString(),
        ["created_at"] = DateTime.UtcNow.ToString("O"),
        ["updated_at"] = DateTime.UtcNow.ToString("O"),
    };

    var holesRaw = form["holes_json"].ToString().Trim();
    if (holesRaw.Length > 0)
    {
        try { frame["holes_metadata"] = JsonNode.Parse(holesRaw); }
        catch { return Results.BadRequest(new { error = "holes_json is not valid JSON" }); }
    }

    FramesOfShape(cfg, shape).Add(frame);
    admin.Save(cfg);
    return Results.Ok(frame);
});

app.MapPut("/admin/frames/{id:long}", async (long id, HttpRequest req) =>
{
    var body = await req.ReadFromJsonAsync<JsonObject>();
    if (body is null) return Results.BadRequest(new { error = "invalid payload" });

    var cfg = admin.Load();
    var frame = FindFrame(cfg, id);
    if (frame is null) return Results.NotFound(new { error = "frame not found" });

    if (body["name"] is JsonValue nameV && nameV.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
        frame["name"] = name.Trim();
    if (body["is_active"] is JsonValue activeV && activeV.TryGetValue<bool>(out var active))
        frame["is_active"] = active;
    if (body["category_id"] is JsonValue catV && catV.TryGetValue<long>(out var catId))
        frame["category_id"] = catId;
    if (body["holes_metadata"] is JsonNode holes)
        frame["holes_metadata"] = holes.DeepClone();
    if (body["shape"] is JsonValue shapeV && shapeV.TryGetValue<string>(out var newShapeRaw))
    {
        var newShape = newShapeRaw.Trim().ToLowerInvariant();
        if (newShape is not ("square" or "grid" or "strip")) return Results.BadRequest(new { error = "shape must be square|grid|strip" });
        var oldShape = frame["frame"]?.GetValue<string>() ?? "square";
        if (newShape != oldShape)
        {
            var clone = frame.DeepClone().AsObject();
            clone["frame"] = newShape;
            RemoveFrame(cfg, id);
            FramesOfShape(cfg, newShape).Add(clone);
            frame = clone;
        }
    }
    frame["updated_at"] = DateTime.UtcNow.ToString("O");
    admin.Save(cfg);
    return Results.Ok(frame);
});

app.MapDelete("/admin/frames/{id:long}", (long id) =>
{
    var cfg = admin.Load();
    var existing = FindFrame(cfg, id);
    if (existing is null) return Results.NotFound(new { error = "frame not found" });
    RemoveFrame(cfg, id);
    admin.Save(cfg);
    admin.TryDeleteFrameFile(id);
    return Results.Ok(new { deleted = id });
});

app.MapGet("/admin/layouts", () =>
{
    var cfg = admin.Load();
    return Results.Ok(cfg["layouts"] ?? new JsonArray());
});

app.MapPost("/admin/layouts", async (HttpRequest req) =>
{
    var body = await req.ReadFromJsonAsync<JsonObject>();
    if (body is null) return Results.BadRequest(new { error = "invalid payload" });

    var name = body["name"]?.GetValue<string>()?.Trim();
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "name required" });

    var cfg = admin.Load();
    var layouts = cfg["layouts"] as JsonArray ?? new JsonArray();
    var newId = AdminStore.NextId(layouts);

    var layout = new JsonObject
    {
        ["id"] = newId,
        ["name"] = name,
        ["frame_shape"] = body["frame_shape"]?.GetValue<string>() ?? "square",
        ["render_mode"] = body["render_mode"]?.GetValue<string>() ?? "2x2",
        ["layout_mode"] = body["render_mode"]?.GetValue<string>() ?? "2x2",
        ["slug"] = body["slug"]?.GetValue<string>() ?? name.ToLowerInvariant().Replace(' ', '-'),
        ["max_photos"] = Math.Clamp(body["max_photos"]?.GetValue<int>() ?? 4, 1, 16),
        ["capture_count"] = body["capture_count"]?.GetValue<int>() ?? 8,
        ["frame_count"] = body["frame_count"]?.GetValue<int>() ?? 1,
        ["price"] = Math.Max(0, body["price"]?.GetValue<int>() ?? 0),
        ["is_active"] = body["is_active"]?.GetValue<bool>() ?? true,
        ["is_split"] = body["is_split"]?.GetValue<bool>() ?? false,
        ["use_holes_auto_capture"] = body["use_holes_auto_capture"]?.GetValue<bool>() ?? true,
        ["has_assigned_frames"] = false,
        ["created_at"] = DateTime.UtcNow.ToString("O"),
        ["updated_at"] = DateTime.UtcNow.ToString("O"),
    };
    layouts.Add(layout);
    cfg["layouts"] = layouts;
    admin.Save(cfg);
    return Results.Ok(layout);
});

app.MapPut("/admin/layouts/{id:long}", async (long id, HttpRequest req) =>
{
    var body = await req.ReadFromJsonAsync<JsonObject>();
    if (body is null) return Results.BadRequest(new { error = "invalid payload" });

    var cfg = admin.Load();
    if (cfg["layouts"] is not JsonArray layouts) return Results.NotFound(new { error = "no layouts" });

    foreach (var item in layouts)
    {
        if (item is not JsonObject o) continue;
        if (o["id"] is not JsonValue v || !v.TryGetValue<long>(out var lid) || lid != id) continue;

        foreach (var key in new[] { "name", "frame_shape", "render_mode", "layout_mode", "slug" })
            if (body[key] is JsonValue sv && sv.TryGetValue<string>(out var s))
                o[key] = s;
        foreach (var key in new[] { "max_photos", "capture_count", "frame_count", "price" })
            if (body[key] is JsonValue nv && nv.TryGetValue<int>(out var n))
                o[key] = key == "max_photos" ? Math.Clamp(n, 1, 16) : Math.Max(0, n);
        foreach (var key in new[] { "is_active", "is_split", "use_holes_auto_capture" })
            if (body[key] is JsonValue bv && bv.TryGetValue<bool>(out var b))
                o[key] = b;
        o["updated_at"] = DateTime.UtcNow.ToString("O");
        admin.Save(cfg);
        return Results.Ok(o);
    }
    return Results.NotFound(new { error = "layout not found" });
});

app.MapDelete("/admin/layouts/{id:long}", (long id) =>
{
    var cfg = admin.Load();
    if (cfg["layouts"] is not JsonArray layouts) return Results.NotFound(new { error = "no layouts" });
    for (var i = layouts.Count - 1; i >= 0; i--)
    {
        if (layouts[i] is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<long>(out var lid) && lid == id)
        {
            layouts.RemoveAt(i);
            admin.Save(cfg);
            return Results.Ok(new { deleted = id });
        }
    }
    return Results.NotFound(new { error = "layout not found" });
});

app.MapGet("/admin/categories", () =>
{
    var cfg = admin.Load();
    return Results.Ok(cfg["categories"] ?? new JsonArray());
});

app.MapPost("/admin/categories", async (HttpRequest req) =>
{
    var body = await req.ReadFromJsonAsync<JsonObject>();
    var name = body?["name"]?.GetValue<string>()?.Trim();
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "name required" });

    var cfg = admin.Load();
    var categories = cfg["categories"] as JsonArray ?? new JsonArray();
    var cat = new JsonObject
    {
        ["id"] = AdminStore.NextId(categories),
        ["name"] = name,
        ["created_at"] = DateTime.UtcNow.ToString("O"),
        ["updated_at"] = DateTime.UtcNow.ToString("O"),
    };
    categories.Add(cat);
    cfg["categories"] = categories;
    admin.Save(cfg);
    return Results.Ok(cat);
});

app.MapDelete("/admin/categories/{id:long}", (long id) =>
{
    var cfg = admin.Load();
    if (cfg["categories"] is not JsonArray categories) return Results.NotFound(new { error = "no categories" });
    for (var i = categories.Count - 1; i >= 0; i--)
    {
        if (categories[i] is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<long>(out var cid) && cid == id)
        {
            categories.RemoveAt(i);
            admin.Save(cfg);
            return Results.Ok(new { deleted = id });
        }
    }
    return Results.NotFound(new { error = "category not found" });
});

// Frame image bytes for the admin UI thumbnails (token-gated with the rest of the API).
app.MapGet("/admin/framefile/{id:long}", (long id) =>
{
    var cfg = admin.Load();
    var frame = FindFrame(cfg, id);
    var url = frame?["url"]?.GetValue<string>() ?? frame?["path"]?.GetValue<string>();
    if (url is null || !File.Exists(url)) return Results.NotFound(new { error = "frame file not found" });
    var ext = Path.GetExtension(url).TrimStart('.').ToLowerInvariant();
    var mime = ext is "jpg" or "jpeg" ? "image/jpeg" : ext == "webp" ? "image/webp" : "image/png";
    return Results.File(url, mime);
});

app.MapGet("/admin/logs", (int? tail) =>
{
    var lines = tail is > 0 and <= 500 ? tail.Value : 200;
    var logsDir = settings.LogsDir;
    var today = Path.Combine(logsDir, $"bridge-{DateTime.Now:yyyyMMdd}.log");
    if (!File.Exists(today)) return Results.Ok(Array.Empty<string>());
    var all = File.ReadAllLines(today);
    return Results.Ok(all.Skip(Math.Max(0, all.Length - lines)));
});

// Serve the admin UI (static build). API calls still require the bridge token.
static string? ResolveAdminDist()
{
    var env = Environment.GetEnvironmentVariable("CLICKFUN_ADMIN_DIST");
    if (!string.IsNullOrWhiteSpace(env) && File.Exists(Path.Combine(env, "index.html"))) return env;

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 8 && dir is not null; i++)
    {
        var candidate = Path.Combine(dir.FullName, "app", "admin", "dist");
        if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
        candidate = Path.Combine(dir.FullName, "admin", "dist");
        if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
        dir = dir.Parent;
    }
    return null;
}

var adminDist = ResolveAdminDist();
if (adminDist is not null)
{
    // Single self-contained handler: serves the built admin SPA, with traversal
    // protection and SPA fallback to index.html for client-side routes.
    var rootFull = Path.GetFullPath(adminDist);
    static string Mime(string ext) => ext.ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".map" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream",
    };

    async Task ServeAdmin(HttpContext ctx)
    {
        try
        {
            var sub = ctx.Request.RouteValues["path"]?.ToString() ?? "";
            var candidate = string.IsNullOrEmpty(sub) ? Path.Combine(adminDist, "index.html") : Path.Combine(adminDist, sub);
            var full = Path.GetFullPath(candidate);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                full = Path.Combine(adminDist, "index.html");
            }

            ctx.Response.ContentType = Mime(Path.GetExtension(full));
            ctx.Response.Headers.CacheControl = full.EndsWith("index.html", StringComparison.OrdinalIgnoreCase) ? "no-store" : "public, max-age=31536000, immutable";
            var bytes = await File.ReadAllBytesAsync(full);
            await ctx.Response.Body.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            BridgeLog.Error("ServeAdmin failed", ex);
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("admin error: " + ex.Message);
        }
    }

    app.MapGet("/admin", ServeAdmin);
    app.MapGet("/admin/{**path}", ServeAdmin);
    BridgeLog.Info($"Admin UI served from {adminDist}");
}
else
{
    app.MapGet("/admin", () => Results.Text("Admin UI not built yet. Run: cd app && npm run build:admin", "text/plain"));
    BridgeLog.Warn("Admin UI dist not found — /admin returns a placeholder.");
}

BridgeLog.Info($"Listening on http://127.0.0.1:{port} (token auth on, health exempted)");
await app.RunAsync();

internal sealed record PreviewRequest(string? ImagePath, string? Filter, string? OverlayTemplate);
