using System.Text.Json;
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

// Token gate for every endpoint except /health.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path == "/health") { await next(); return; }
    if (!ctx.Request.Headers.TryGetValue("X-ClickFun-Token", out var provided) || provided != token)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "missing or invalid X-ClickFun-Token" });
        return;
    }
    await next();
});

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

app.MapGet("/frame/latest", () =>
{
    var frame = camera.GetLatestFrame();
    if (frame is null) return Results.NoContent();
    return Results.Bytes(frame.Value.Jpeg, "image/jpeg");
});

app.MapPost("/capture", async () =>
{
    var result = await camera.CaptureAsync();
    return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: 502);
});

app.MapPost("/preview", async (HttpRequest req) =>
{
    // Applies filter + overlay to an existing capture and returns the processed JPEG.
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

app.MapGet("/settings", () => Results.Ok(settings.Load()));

app.MapPut("/settings", async (HttpRequest req) =>
{
    var dto = await req.ReadFromJsonAsync<SettingsDto>();
    if (dto is null) return Results.BadRequest(new { error = "invalid settings payload" });
    return Results.Ok(settings.Save(dto));
});

app.MapPost("/print", async (HttpRequest req) =>
{
    var request = await req.ReadFromJsonAsync<PrintRequest>();
    if (request is null) return Results.BadRequest(new { error = "invalid print request" });
    var printer = new PrintService(settings);
    var result = printer.Print(request);
    return result.Success ? Results.Ok(result) : Results.Problem(result.Message, statusCode: 502);
});

BridgeLog.Info($"Listening on http://127.0.0.1:{port} (token auth on, health exempted)");
await app.RunAsync();

internal sealed record PreviewRequest(string? ImagePath, string? Filter, string? OverlayTemplate);
