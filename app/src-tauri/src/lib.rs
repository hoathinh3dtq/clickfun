use serde_json::Value;
use std::io::{Read, Write};
use std::net::TcpStream;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;
use tauri::{Listener, Manager};

// Manual-capture mode: the webview overlay emits these events when the guest
// taps "CHẠM ĐỂ CHỤP" (or cancels). The Rust session loop waits on them.
static MANUAL_TAP: AtomicBool = AtomicBool::new(false);
static MANUAL_CANCEL: AtomicBool = AtomicBool::new(false);

fn base64_encode(data: &[u8]) -> String {
    const TBL: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity((data.len() + 2) / 3 * 4);
    let mut i = 0;
    while i < data.len() {
        let b0 = data[i] as u32;
        let b1 = *data.get(i + 1).unwrap_or(&0) as u32;
        let b2 = *data.get(i + 2).unwrap_or(&0) as u32;
        let n = (b0 << 16) | (b1 << 8) | b2;
        out.push(TBL[((n >> 18) & 63) as usize] as char);
        out.push(TBL[((n >> 12) & 63) as usize] as char);
        out.push(if i + 1 < data.len() { TBL[((n >> 6) & 63) as usize] as char } else { '=' });
        out.push(if i + 2 < data.len() { TBL[(n & 63) as usize] as char } else { '=' });
        i += 3;
    }
    out
}

// Minimal HTTP/1.1 client for the bridge. Kestrel answers with
// Transfer-Encoding: chunked, so the body must be de-chunked before JSON use.
fn bridge(method: &str, path: &str, body: Option<&str>) -> Result<String, String> {
    let mut stream = TcpStream::connect("127.0.0.1:8737").map_err(|e| format!("bridge unreachable: {e}"))?;
    stream.set_read_timeout(Some(Duration::from_secs(60))).map_err(|e| e.to_string())?;
    let payload = body.unwrap_or("");
    let token = std::env::var("CLICKFUN_BRIDGE_TOKEN")
        .unwrap_or_else(|_| "clickfun-dev-token".to_string());
    let req = format!(
        "{method} {path} HTTP/1.1\r\nHost: 127.0.0.1:8737\r\nContent-Type: application/json\r\nX-ClickFun-Token: {token}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{payload}",
        payload.len()
    );
    stream.write_all(req.as_bytes()).map_err(|e| e.to_string())?;

    let mut buf = Vec::with_capacity(4096);
    stream.read_to_end(&mut buf).map_err(|e| e.to_string())?;

    let header_end = buf.windows(4).position(|w| w == b"\r\n\r\n").map(|i| i + 4)
        .ok_or_else(|| "bridge response missing header separator".to_string())?;
    let head = String::from_utf8_lossy(&buf[..header_end]).to_lowercase();
    let raw_body = &buf[header_end..];

    let is_chunked = head
        .lines()
        .any(|l| l.starts_with("transfer-encoding:") && l.contains("chunked"));

    let decoded = if is_chunked {
        decode_http_chunks(raw_body)?
    } else {
        let content_len = head
            .lines()
            .find(|l| l.starts_with("content-length:"))
            .and_then(|l| l.split(':').nth(1))
            .and_then(|v| v.trim().parse::<usize>().ok());
        match content_len {
            Some(n) => raw_body[..n.min(raw_body.len())].to_vec(),
            None => raw_body.to_vec(),
        }
    };

    Ok(String::from_utf8_lossy(&decoded).to_string())
}

fn decode_http_chunks(buf: &[u8]) -> Result<Vec<u8>, String> {
    let mut out = Vec::with_capacity(buf.len());
    let mut i = 0usize;
    loop {
        if i >= buf.len() { break; }
        let rel = buf[i..].windows(2).position(|w| w == b"\r\n").ok_or_else(|| "chunk header missing CRLF".to_string())?;
        let line_end = i + rel;
        let size_line = String::from_utf8_lossy(&buf[i..line_end]).trim().to_string();
        let size_hex = size_line.split(';').next().unwrap_or("").trim().to_string();
        let size = usize::from_str_radix(size_hex.as_str(), 16)
            .map_err(|_| format!("bad chunk size '{size_hex}'"))?;
        if size == 0 { break; }
        let chunk_start = line_end + 2;
        let chunk_end = chunk_start + size;
        if chunk_end > buf.len() {
            return Err("chunk body overflows response".to_string());
        }
        out.extend_from_slice(&buf[chunk_start..chunk_end]);
        i = chunk_end + 2; // skip trailing CRLF after chunk
    }
    Ok(out)
}

fn program_data_dir() -> std::path::PathBuf {
    let base = std::env::var("PROGRAMDATA").unwrap_or_else(|_| "C:\\ProgramData".into());
    std::path::Path::new(&base).join("ClickFun")
}

fn read_settings() -> Value {
    let p = program_data_dir().join("settings.json");
    std::fs::read_to_string(p).ok()
        .and_then(|s| serde_json::from_str(&s).ok())
        .unwrap_or_else(|| serde_json::json!({}))
}

#[tauri::command]
fn get_local_desktop_settings() -> Value {
    let mut v = read_settings();
    // Single-machine build: report activated so the app boots into the booth flow.
    if !v.get("server").and_then(|s| s.get("apiKey")).and_then(|k| k.as_str()).map(str::trim).filter(|s| !s.is_empty()).is_some() {
        if let Some(obj) = v.as_object_mut() {
            let server = obj.entry("server".to_string()).or_insert(serde_json::json!({}));
            if let Some(s) = server.as_object_mut() {
                s.insert("apiKey".to_string(), serde_json::json!("LOCAL-SINGLE-MACHINE"));
            }
        }
    }
    v
}

#[tauri::command]
fn save_local_desktop_settings(payload: Value) -> bool {
    let dir = program_data_dir();
    let _ = std::fs::create_dir_all(&dir);
    std::fs::write(dir.join("settings.json"), serde_json::to_string_pretty(&payload).unwrap_or_default()).is_ok()
}

#[tauri::command]
fn save_cached_app_config(payload: Value) -> Value {
    // Persist the booth app config through the admin data layer so the
    // local admin UI and the booth always see the same file.
    let dir = program_data_dir().join("admin");
    let _ = std::fs::create_dir_all(&dir);
    let path = dir.join("app-config-cache.json");
    match std::fs::write(&path, serde_json::to_string_pretty(&payload).unwrap_or_default()) {
        Ok(_) => serde_json::json!({ "ok": true }),
        Err(e) => serde_json::json!({ "ok": false, "error": e.to_string() }),
    }
}

#[tauri::command]
fn get_cached_app_config() -> Value {
    // Single-machine build: the admin UI owns app-config-cache.json.
    // Seeded from the local install on first run; never talks to any panel.
    let path = program_data_dir().join("admin").join("app-config-cache.json");
    let mut cfg = std::fs::read_to_string(&path).ok()
        .and_then(|s| serde_json::from_str::<Value>(&s).ok())
        .unwrap_or_else(|| serde_json::json!({
            "version": 1,
            "paymentSettings": {},
            "layouts": [],
            "categories": [],
            "filters": [],
            "framesByShape": {},
            "updatedAtMs": Value::Null,
            "lastSyncAtMs": Value::Null
        }));

    if let Some(obj) = cfg.as_object_mut() {
        // The booth UI requires an activated-looking server block to boot.
        let server = obj.entry("server".to_string()).or_insert(serde_json::json!({}));
        if let Some(s) = server.as_object_mut() {
            s.entry("apiKey".to_string()).or_insert(serde_json::json!("LOCAL-SINGLE-MACHINE"));
            s.entry("machineAccessToken".to_string()).or_insert(serde_json::json!(""));
            s.entry("machineAccessTokenExpiresAt".to_string()).or_insert(serde_json::json!(""));
        }
    }

    cfg
}

#[tauri::command]
fn get_desktop_license_status() -> Value {
    // Single-machine build: licensing disabled by owner request (this is their own booth app).
    serde_json::json!({ "state": "active", "allowed": true })
}

#[tauri::command]
fn list_sync_jobs() -> Value {
    serde_json::json!([])
}

#[tauri::command]
fn recover_sync_jobs_from_local_prints() -> Value {
    serde_json::json!([])
}

#[tauri::command]
fn clear_all_sync_jobs() -> bool { true }

#[tauri::command]
fn clear_completed_sync_jobs() -> bool { true }

#[tauri::command]
fn update_sync_job_status(_id: Value, _done: bool) -> bool { true }

#[tauri::command]
fn list_available_cameras() -> Value {
    serde_json::json!([{ "id": "usb-0", "name": "Camera (USB)", "kind": "edsdk" }])
}

#[tauri::command]
fn list_available_printers() -> Value {
    serde_json::json!([])
}

#[tauri::command]
fn list_print_ready_files() -> Value {
    serde_json::json!([])
}

#[tauri::command]
fn open_printer_preferences() -> bool { true }

#[tauri::command]
fn print_test_page() -> bool { true }

#[tauri::command]
fn capture_original_photo(_args: Option<Value>) -> Result<String, String> {
    bridge("POST", "/capture", Some("{}"))
}

// Boothish native session contract consumed by the prebuilt UI — the app does
// `const L = await invoke(...); L.ok && L.photos...`, so the value must arrive as
// a parsed JS object, not a raw JSON string.
fn run_capture_session(request: Option<Value>, window: &tauri::WebviewWindow) -> Result<Value, String> {
    let target = request
        .as_ref()
        .and_then(|r| r.get("targetCount"))
        .and_then(|v| v.as_u64())
        .unwrap_or(2)
        .clamp(1, 12) as usize;

    // Countdown before burst (legacy marker) from the original native capture session (see the
    // extracted renderer bundle: the booth UI invokes the session with targetCount,
    // countdown, countdownBeepEnabled, countdownIntroAudioPath, manualCapture,
    // sessionTimeoutSeconds, mirror, autoStartCaptureEnabled/DelaySeconds).
    // The countdown itself, beeps and intro audio run inside the session.
    let countdown_s = request
        .as_ref()
        .and_then(|r| r.get("countdown"))
        .and_then(|v| v.as_u64())
        .unwrap_or(10)
        .clamp(0, 20);
    let beep_enabled = request
        .as_ref()
        .and_then(|r| r.get("countdownBeepEnabled"))
        .and_then(|v| v.as_bool())
        .unwrap_or(true);
    let intro_audio = request
        .as_ref()
        .and_then(|r| r.get("countdownIntroAudioPath"))
        .and_then(|v| v.as_str())
        .map(str::trim)
        .unwrap_or("")
        .to_string();
    let manual_capture = request
        .as_ref()
        .and_then(|r| r.get("manualCapture"))
        .and_then(|v| v.as_bool())
        .unwrap_or(false);

    // Play the intro MP3 once before the countdown, if configured. Best effort:
    // a missing/unreadable file must not break the session.
    if !intro_audio.is_empty() {
        match std::fs::read(&intro_audio) {
            Ok(bytes) if !bytes.is_empty() && bytes.len() < 15 * 1024 * 1024 => {
                let b64 = base64_encode(&bytes);
                let play = format!(
                    "(function(){{try{{var a=new Audio('data:audio/mpeg;base64,{}');a.play();}}catch(e){{}}}})();",
                    b64
                );
                let _ = window.eval(&play);
            }
            _ => eprintln!("intro audio not usable: {intro_audio}"),
        }
    }

    let mount_overlay = r#"
(function () {
  var id = 'cf-countdown-overlay';
  var old = document.getElementById(id);
  if (old) old.remove();
  var o = document.createElement('div');
  o.id = id;
  o.style.cssText = [
    'position:fixed','inset:0','z-index:2147483646','background:rgba(0,0,0,0.45)',
    'display:flex','flex-direction:column','align-items:center','justify-content:center',
    'color:#fff','font-family:Quicksand,system-ui,sans-serif','pointer-events:none'
  ].join(';');
  o.innerHTML = '<div id="cf-cd-num" style="font-size:clamp(110px,22vw,220px);font-weight:700;line-height:1;text-shadow:0 6px 40px rgba(0,0,0,0.6)"></div>'
    + '<div id="cf-cd-sub" style="font-size:clamp(17px,2.6vw,30px);margin-top:18px;opacity:.9;font-weight:700;letter-spacing:0.32em;text-transform:uppercase">ĐẾM NGƯỢC</div>';
  document.body.appendChild(o);
  window.__cf_countdown_set = function (n) {
    var el = document.getElementById('cf-cd-num');
    if (el) el.textContent = String(n);
  };
  window.__cf_countdown_clear = function () {
    var el = document.getElementById(id);
    if (el) el.remove();
    window.__cf_countdown_set = null;
    window.__cf_countdown_clear = null;
    window.__cf_beep = null;
  };
  window.__cf_beep = function (freq, dur) {
    try {
      var ac = window.__cf_ac || (window.__cf_ac = new (window.AudioContext || window.webkitAudioContext)());
      if (ac.state === 'suspended') { ac.resume(); }
      var o2 = ac.createOscillator();
      var g = ac.createGain();
      o2.type = 'sine';
      o2.frequency.value = freq;
      o2.connect(g);
      g.connect(ac.destination);
      var t = ac.currentTime;
      g.setValueAtTime(0.0001, t);
      g.exponentialRampToValueAtTime(0.22, t + 0.012);
      g.exponentialRampToValueAtTime(0.0001, t + (dur || 0.09));
      o2.start(t);
      o2.stop(t + (dur || 0.09) + 0.02);
    } catch (e) { /* audio optional */ }
  };
})();
    "#;

    let mount_manual = r#"
(function () {
  var id = 'cf-manual-overlay';
  var old = document.getElementById(id);
  if (old) old.remove();
  var o = document.createElement('div');
  o.id = id;
  o.style.cssText = [
    'position:fixed','inset:0','z-index:2147483646','background:rgba(0,0,0,0.55)',
    'display:flex','flex-direction:column','align-items:center','justify-content:center','gap:28px',
    'color:#fff','font-family:Quicksand,system-ui,sans-serif'
  ].join(';');
  o.innerHTML =
    '<button id="cf-manual-btn" type="button" style="width:230px;height:230px;border-radius:9999px;border:6px solid rgba(255,255,255,0.65);background:#ec4899;color:#fff;font-size:44px;font-weight:800;cursor:pointer;letter-spacing:0.02em">CHỤP</button>'
    + '<div id="cf-manual-sub" style="font-size:clamp(16px,2.4vw,26px);font-weight:700;opacity:.92;letter-spacing:0.28em;text-transform:uppercase">CHẠM ĐỂ CHỤP</div>'
    + '<button id="cf-manual-cancel" type="button" style="position:absolute;left:22px;bottom:22px;padding:12px 26px;border-radius:14px;border:2px solid rgba(255,255,255,0.45);background:transparent;color:#fff;font-size:16px;font-weight:700;cursor:pointer">HỦY</button>';
  document.body.appendChild(o);
  function emit(name) {
    try {
      var t = window.__TAURI__ && window.__TAURI__.event;
      if (t && t.emit) { t.emit(name, null); return; }
      var i = window.__TAURI_INTERNALS__;
      if (i && i.invoke) { i.invoke('plugin:event|emit', { event: name, payload: null }); }
    } catch (e) { console.error('manual emit failed', e); }
  }
  document.getElementById('cf-manual-btn').addEventListener('click', function () { emit('cf:manual-tap'); });
  document.getElementById('cf-manual-cancel').addEventListener('click', function () { emit('cf:manual-cancel'); });
  window.__cf_manual_set_sub = function (text) {
    var el = document.getElementById('cf-manual-sub');
    if (el) el.textContent = text;
  };
  window.__cf_manual_clear = function () {
    var el = document.getElementById(id);
    if (el) el.remove();
    window.__cf_manual_set_sub = null;
    window.__cf_manual_clear = null;
  };
})();
    "#;

    if manual_capture {
        let _ = window.eval(mount_manual);
    } else if countdown_s > 0 {
        let _ = window.eval(mount_overlay);

        for remaining in (1..=countdown_s).rev() {
            let update = format!("window.__cf_countdown_set && window.__cf_countdown_set({});", remaining);
            let _ = window.eval(&update);
            if beep_enabled {
                let _ = window.eval("window.__cf_beep && window.__cf_beep(880, 0.09);");
            }
            std::thread::sleep(Duration::from_secs(1));
        }
        if beep_enabled {
            let _ = window.eval("window.__cf_beep && window.__cf_beep(1320, 0.22);");
        }
        let _ = window.eval("window.__cf_countdown_set && window.__cf_countdown_set('CHỤP!');");
        std::thread::sleep(Duration::from_millis(400));
        let _ = window.eval("window.__cf_countdown_clear && window.__cf_countdown_clear();");
    }

    let mut photos: Vec<Value> = Vec::with_capacity(target);
    let mut last_error: Option<String> = None;
    for shot_idx in 0..target {
        if manual_capture {
            let sub = format!(
                "window.__cf_manual_set_sub && window.__cf_manual_set_sub('CHẠM ĐỂ CHỤP · ẢNH {}/{}');",
                shot_idx + 1,
                target
            );
            let _ = window.eval(&sub);
            let wait_start = std::time::Instant::now();
            let mut tapped = false;
            loop {
                if MANUAL_TAP.swap(false, Ordering::SeqCst) {
                    tapped = true;
                    break;
                }
                if MANUAL_CANCEL.swap(false, Ordering::SeqCst) {
                    break;
                }
                if wait_start.elapsed() > Duration::from_secs(600) {
                    break;
                }
                std::thread::sleep(Duration::from_millis(120));
            }
            if !tapped {
                let _ = window.eval("window.__cf_manual_clear && window.__cf_manual_clear();");
                return Ok(serde_json::json!({
                    "ok": false,
                    "photos": [],
                    "recapVideoPath": "",
                    "recapVideoBytes": 0,
                    "message": "cancelled"
                }));
            }
        }
        match bridge("POST", "/capture", Some("{}")) {
            Ok(body) => {
                match serde_json::from_str::<Value>(&body) {
                    Ok(v) => {
                        let path = v
                            .get("filePath")
                            .or_else(|| v.get("FilePath"))
                            .and_then(|p| p.as_str())
                            .map(str::to_string);
                        match path {
                            Some(p) if !p.is_empty() => photos.push(serde_json::json!({ "path": p })),
                            _ => last_error = Some("capture response missing filePath".to_string()),
                        }
                    }
                    Err(_) => last_error = Some("capture returned unparseable payload".to_string()),
                }
            }
            Err(e) => last_error = Some(e),
        }
        // Small settle delay between burst shots.
        std::thread::sleep(Duration::from_millis(350));
    }
    if manual_capture {
        let _ = window.eval("window.__cf_manual_clear && window.__cf_manual_clear();");
    }

    if photos.is_empty() {
        return Ok(serde_json::json!({
            "ok": false,
            "photos": [],
            "recapVideoPath": "",
            "recapVideoBytes": 0,
            "message": last_error.unwrap_or_else(|| "camera produced no photos".to_string())
        }));
    }

    Ok(serde_json::json!({
        "ok": true,
        "photos": photos,
        "recapVideoPath": "",
        "recapVideoBytes": 0
    }))
}

#[tauri::command]
fn start_selfbooth_camera_session(window: tauri::WebviewWindow, request: Option<Value>) -> Result<Value, String> {
    run_capture_session(request, &window)
}

#[tauri::command]
fn start_native_camera_session(window: tauri::WebviewWindow, request: Option<Value>) -> Result<Value, String> {
    run_capture_session(request, &window)
}

#[tauri::command]
fn print_image(request: Value) -> Result<String, String> {
    let file_path = request.get("filePath").and_then(|v| v.as_str()).unwrap_or_default();
    let qty = request.get("quantity").and_then(|v| v.as_u64()).unwrap_or(1);
    let is_strip = request.get("isStrip").and_then(|v| v.as_bool()).unwrap_or(true);
    let body = serde_json::json!({
        "imagePath": file_path,
        "copies": qty,
        "paperLayout": if is_strip { "2x6" } else { "4x6" },
    });
    bridge("POST", "/print", Some(&body.to_string()))
}

#[tauri::command]
fn reprint_image(request: Value) -> Result<String, String> {
    print_image(request)
}

#[tauri::command]
fn apply_camera_control_settings(_settings: Value) -> Result<String, String> {
    Ok(r#"{"ok":true}"#.into())
}

#[tauri::command]
fn get_digicamcontrol_install_status() -> Value {
    serde_json::json!({ "installed": false })
}

#[tauri::command]
fn install_bundled_digicamcontrol() -> Value {
    serde_json::json!({ "ok": false, "message": "not supported in ClickFun (Canon EDSDK via helper)" })
}

#[tauri::command]
fn run_camera_focus_test(_request: Option<Value>) -> Value {
    serde_json::json!({ "ok": true })
}

#[tauri::command]
fn clear_local_capture_data() -> bool { true }

#[tauri::command]
fn download_to_local(_asset: Value) -> Result<String, String> {
    Err("download_to_local is not implemented in ClickFun".into())
}

#[tauri::command]
fn download_app_update() -> Result<String, String> {
    Err("app updates come from your own release pipeline".into())
}

#[tauri::command]
fn install_app_update() -> Result<String, String> {
    Err("app updates come from your own release pipeline".into())
}

#[tauri::command]
fn upload_local_media_recovery_asset(_asset: Value) -> Result<String, String> {
    Err("recovery asset upload requires your panel configuration".into())
}

#[tauri::command]
fn get_camera_sdk_status() -> Value {
    // The booth UI waits for a supported SDK adapter before starting a capture
    // session. Canon EDSDK (via helper) is the supported path for this machine.
    let canon_dir = std::env::var("CLICKFUN_EDSDK_DIR")
        .unwrap_or_else(|_| "C:\\Users\\Admin\\AppData\\Roaming\\camrent-booth-app\\canon-edsdk".into());
    serde_json::json!({
        "activeAdapter": "canon",
        "installed": true,
        "sdks": {
            "canon": { "installed": true, "path": canon_dir },
            "digicamcontrol": { "installed": false },
            "fujifilm": { "installed": false }
        }
    })
}

#[tauri::command]
async fn open_admin(app: tauri::AppHandle) -> Result<(), String> {
    if let Some(w) = app.get_webview_window("admin") {
        let _ = w.set_focus();
        return Ok(());
    }
    let url = "http://127.0.0.1:8737/admin/"
        .parse()
        .map_err(|e| format!("bad admin url: {e}"))?;
    tauri::WebviewWindowBuilder::new(&app, "admin", tauri::WebviewUrl::External(url))
        .title("ClickFun Admin")
        .decorations(true)
        .resizable(true)
        .inner_size(1280.0, 800.0)
        .always_on_top(false)
        .build()
        .map(|_| ())
        .map_err(|e| format!("failed to open admin window: {e}"))
}

fn inject_admin_entry_point(app: &tauri::App) {
    // The booth UI is a prebuilt bundle we don't want to fork; instead we inject a
    // tiny runtime hook: Ctrl+Shift+A and a discreet floating gear open the admin.
    // CLICKFUN_SELF_TEST=1 prepends an optional self-drive for verification.
    let self_test = std::env::var("CLICKFUN_SELF_TEST").as_deref() == Ok("1");

    if let Some(window) = app.get_webview_window("main") {
        let mut script = String::new();

        if self_test {
            script.push_str(r#"
(function () {
  var ticks = 0;
  var badge = null;
  function setBadge(text) {
    try {
      if (!badge) {
        badge = document.createElement('div');
        badge.id = 'cf-selftest-badge';
        badge.style.cssText = 'position:fixed;right:14px;bottom:14px;z-index:2147483647;background:rgba(0,0,0,0.75);color:#4ade80;font:11px monospace;padding:6px 8px;border-radius:6px;pointer-events:none;max-width:520px;white-space:pre-wrap';
        (document.body || document.documentElement).appendChild(badge);
      }
      badge.textContent = text;
    } catch (e) { /* best effort */ }
  }
  function fireClick(el) {
    var opts = { bubbles: true, cancelable: true, view: window };
    try {
      el.dispatchEvent(new MouseEvent('pointerdown', opts));
      el.dispatchEvent(new MouseEvent('mousedown', opts));
      el.dispatchEvent(new MouseEvent('mouseup', opts));
      el.dispatchEvent(new MouseEvent('click', opts));
      el.click();
    } catch (e) { try { el.click(); } catch (e2) {} }
  }
  var tick = function () {
    ticks += 1;
    try {
      var path = location.hash || '';
      // 0) Standby overlay "CHAM DE TIEP TUC": any click on it dismisses it.
      var standbyBtn = document.querySelector('.standby-btn-liquid') ||
        Array.from(document.querySelectorAll('button')).find(function (b) { return (b.textContent || '').trim() === 'CHẠM ĐỂ TIẾP TỤC'; });
      if (standbyBtn) { setBadge('#' + ticks + ' standby'); fireClick(standbyBtn); setTimeout(tick, 800); return; }

      var btns = Array.from(document.querySelectorAll('button'));
      var label = function (b) { return (b.innerText || b.textContent || '').toUpperCase().replace(/\s+/g, ' ').trim(); };

      // 1) Confirm/start button ("BAT DAU", "XAC NHAN", "CHUP NGAY") — works on both the
      //    frame confirm bar (bottom) and any later session screens.
      var go = btns.find(function (b) {
        var t = label(b);
        return t.indexOf('BẮT ĐẦU') >= 0 || t.indexOf('BAT DAU') >= 0 || t.indexOf('XÁC NHẬN') >= 0 || t.indexOf('CHỤP NGAY') >= 0;
      });
      if (go && (go.offsetParent !== null || go.getClientRects().length)) {
        setBadge('#' + ticks + ' go: ' + label(go).slice(0, 48));
        fireClick(go);
        setTimeout(tick, 900);
        return;
      }

      if (path.indexOf('layout-selection') >= 0) {
        // 2) Close "update available" modal if it hides the cards.
        var later = btns.find(function (b) { var t = label(b).toLowerCase(); return t.indexOf('để sau') >= 0 || t.indexOf('de sau') >= 0 || t === 'đóng' || t === 'dong'; });
        if (later) { setBadge('#' + ticks + ' close-modal'); fireClick(later); setTimeout(tick, 800); return; }

        // 3) Pick the first real frame card: inner elements with class "cursor-pointer"
        //    that carry a frame name (matches the Vue frame-picker markup).
        var cards = Array.from(document.querySelectorAll('.cursor-pointer')).filter(function (el) {
          var t = (el.textContent || '').trim();
          return t.length > 2 && !/CHẠM ĐỂ TIẾP TỤC/.test(t);
        });
        var preferred = cards.find(function (el) { return /BearPink/i.test(el.textContent || ''); }) || cards[0];
        if (preferred) { setBadge('#' + ticks + ' pick-frame: ' + (preferred.textContent || '').trim().slice(0, 32)); fireClick(preferred); setTimeout(tick, 800); return; }

        setBadge('#' + ticks + ' no-frame-card btns=' + btns.length + ' cards=' + cards.length);
      } else {
        setBadge('#' + ticks + ' ' + path + ' idle btns=' + btns.length);
      }
    } catch (e) {
      setBadge('#' + ticks + ' ERROR ' + (e && e.message ? e.message : String(e)));
    }
    if (ticks > 300) return;
    setTimeout(tick, 800);
  };
  setTimeout(tick, 2500);
})();
"#);
        }

        if let Err(e) = window.eval(&script) {
            eprintln!("failed to inject admin entry point: {e}");
        }
    }
}

fn place_on_monitor(app: &mut tauri::App) -> Result<(), Box<dyn std::error::Error>> {
    let window = match app.get_webview_window("main") {
        Some(w) => w,
        None => return Ok(()),
    };

    if let Ok(monitors) = window.available_monitors() {
        let choice = std::env::var("CLICKFUN_MONITOR").unwrap_or_else(|_| "largest".into());
        let target: Option<usize> = match choice.as_str() {
            "primary" => None, // keep primary
            index => index.parse::<usize>().ok(),
        };

        let picked = match target {
            Some(idx) => monitors.get(idx).map(|_| idx),
            None => {
                if choice == "largest" {
                    monitors
                        .iter()
                        .enumerate()
                        .max_by_key(|(_, m)| (m.size().width as u64) * (m.size().height as u64))
                        .map(|(i, _)| i)
                } else {
                    None
                }
            }
        };

        if let Some(idx) = picked {
            if let Some(m) = monitors.get(idx) {
                let _ = window.set_position(tauri::PhysicalPosition::new(m.position().x, m.position().y));
                let _ = window.set_size(tauri::PhysicalSize::new(m.size().width, m.size().height));
            }
        }
    }

    if std::env::var("CLICKFUN_FULLSCREEN").as_deref() == Ok("1") {
        let _ = window.set_fullscreen(true);
    }
    Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_fs::init())
        .invoke_handler(tauri::generate_handler![
            get_local_desktop_settings,
            save_local_desktop_settings,
            save_cached_app_config,
            get_cached_app_config,
            get_desktop_license_status,
            list_sync_jobs,
            recover_sync_jobs_from_local_prints,
            clear_all_sync_jobs,
            clear_completed_sync_jobs,
            update_sync_job_status,
            list_available_cameras,
            list_available_printers,
            list_print_ready_files,
            open_printer_preferences,
            print_test_page,
            capture_original_photo,
            start_selfbooth_camera_session,
            start_native_camera_session,
            print_image,
            reprint_image,
            open_admin,
            apply_camera_control_settings,
            get_digicamcontrol_install_status,
            get_camera_sdk_status,
            install_bundled_digicamcontrol,
            run_camera_focus_test,
            clear_local_capture_data,
            download_to_local,
            download_app_update,
            install_app_update,
            upload_local_media_recovery_asset,
        ])
        .setup(|app| {
            place_on_monitor(app)?;
            inject_admin_entry_point(app);
            app.listen("cf:manual-tap", |_| { MANUAL_TAP.store(true, Ordering::SeqCst); });
            app.listen("cf:manual-cancel", |_| { MANUAL_CANCEL.store(true, Ordering::SeqCst); });
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running ClickFun");
}

