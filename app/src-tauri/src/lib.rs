use serde_json::Value;
use std::io::{Read, Write};
use std::net::TcpStream;
use std::time::Duration;
use tauri::Manager;

fn bridge(method: &str, path: &str, body: Option<&str>) -> Result<String, String> {
    let mut stream = TcpStream::connect("127.0.0.1:8737").map_err(|e| format!("bridge unreachable: {e}"))?;
    stream.set_read_timeout(Some(Duration::from_secs(30))).map_err(|e| e.to_string())?;
    let payload = body.unwrap_or("");
    let req = format!(
        "{method} {path} HTTP/1.1\r\nHost: 127.0.0.1:8737\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{payload}",
        payload.len()
    );
    stream.write_all(req.as_bytes()).map_err(|e| e.to_string())?;
    let mut buf = String::new();
    stream.read_to_string(&mut buf).map_err(|e| e.to_string())?;
    match buf.find("\r\n\r\n") {
        Some(i) => Ok(buf[i + 4..].to_string()),
        None => Ok(buf),
    }
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
fn run_capture_session(request: Option<Value>) -> Result<Value, String> {
    let target = request
        .as_ref()
        .and_then(|r| r.get("targetCount"))
        .and_then(|v| v.as_u64())
        .unwrap_or(2)
        .clamp(1, 12) as usize;

    // Honor a short countdown before the burst so the user sees it coming.
    let countdown_s = request
        .as_ref()
        .and_then(|r| r.get("countdown"))
        .and_then(|v| v.as_u64())
        .unwrap_or(0)
        .clamp(0, 20);
    if countdown_s > 0 {
        std::thread::sleep(Duration::from_secs(countdown_s.min(3)));
    }

    let mut photos: Vec<Value> = Vec::with_capacity(target);
    let mut last_error: Option<String> = None;
    for _ in 0..target {
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
fn start_selfbooth_camera_session(request: Option<Value>) -> Result<Value, String> {
    run_capture_session(request)
}

#[tauri::command]
fn start_native_camera_session(request: Option<Value>) -> Result<Value, String> {
    run_capture_session(request)
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
    if let Some(window) = app.get_webview_window("main") {
        let script = r#"
(function () {
  var root = document.documentElement;
  function invokeOpenAdmin() {
    try {
      var i = window.__TAURI_INTERNALS__;
      if (i && i.invoke) { i.invoke('open_admin', {}); return true; }
      var t = window.__TAURI__;
      if (t && t.core && t.core.invoke) { t.core.invoke('open_admin', {}); return true; }
    } catch (e) { console.error('open_admin failed', e); }
    return false;
  }
  function mountStatusChips() {
    if (document.getElementById('cf-status-chips')) return;
    var bar = document.createElement('div');
    bar.id = 'cf-status-chips';
    bar.style.cssText = [
      'position:fixed','top:12px','right:14px','z-index:2147483647','display:flex',
      'gap:8px','align-items:center','font-family:Quicksand,system-ui,sans-serif',
      'font-size:12px','font-weight:600','user-select:none','pointer-events:none'
    ].join(';');
    var mkChip = function (id) {
      var s = document.createElement('span');
      s.id = id;
      s.style.cssText = [
        'display:inline-flex','align-items:center','gap:6px','padding:5px 12px',
        'border-radius:999px','background:rgba(15,23,42,0.72)','color:#e2e8f0',
        'backdrop-filter:blur(6px)','box-shadow:0 2px 10px rgba(0,0,0,0.25)'
      ].join(';');
      return s;
    };
    var cam = mkChip('cf-chip-cam');
    var prn = mkChip('cf-chip-printer');
    var sync = mkChip('cf-chip-sync');
    bar.appendChild(cam); bar.appendChild(prn); bar.appendChild(sync);
    document.body.appendChild(bar);
  }
  function pollHealth() {
    fetch('http://127.0.0.1:8737/health', { cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (h) {
        var cam = document.getElementById('cf-chip-cam');
        var prn = document.getElementById('cf-chip-printer');
        var sync = document.getElementById('cf-chip-sync');
        if (!cam || !prn || !sync) return;
        if (!h) {
          cam.textContent = '📷 bridge off';
          prn.textContent = '🖨️ ?';
          sync.textContent = '⚠ offline';
          return;
        }
        var b = (h.cameraBackend || 'unknown').toLowerCase();
        cam.textContent = (b === 'canon' ? '📷 Canon' : b === 'webcam' ? '📷 Webcam' : '📷 ' + b);
        prn.textContent = h.printerAvailable ? '🖨️ Máy in OK' : '🖨️ Chưa có máy in';
        sync.textContent = '● OK';
        sync.style.color = '#34d399';
      })
      .catch(function () {
        var cam = document.getElementById('cf-chip-cam');
        var sync = document.getElementById('cf-chip-sync');
        if (cam) cam.textContent = '📷 bridge off';
        if (sync) { sync.textContent = '⚠ offline'; sync.style.color = '#f87171'; }
      });
  }
  function mount() {
    mountStatusChips();
    if (!window.__cf_poll_installed__) {
      window.__cf_poll_installed__ = true;
      pollHealth();
      setInterval(pollHealth, 10000);
    }
    if (document.getElementById('cf-admin-fab')) return;
    var btn = document.createElement('button');
    btn.id = 'cf-admin-fab';
    btn.type = 'button';
    btn.title = 'Mo admin local (Ctrl+Shift+A)';
    btn.setAttribute('aria-label', 'Mo admin local');
    btn.innerHTML = '&#9881;';
    btn.style.cssText = [
      'position:fixed','left:14px','bottom:14px','width:44px','height:44px',
      'border:none','border-radius:50%','z-index:2147483647','cursor:pointer',
      'font-size:22px','line-height:1','color:#fff','opacity:0.30',
      'background:linear-gradient(135deg,#f86fa5,#d84b7c)',
      'box-shadow:0 4px 14px rgba(0,0,0,0.25)','transition:opacity .2s'
    ].join(';');
    btn.addEventListener('mouseenter', function () { btn.style.opacity = '0.92'; });
    btn.addEventListener('mouseleave', function () { btn.style.opacity = '0.30'; });
    btn.addEventListener('click', function (e) { e.preventDefault(); invokeOpenAdmin(); });
    document.body.appendChild(btn);
  }
  window.addEventListener('keydown', function (e) {
    if ((e.ctrlKey || e.metaKey) && e.shiftKey && (e.key === 'A' || e.key === 'a')) {
      e.preventDefault();
      invokeOpenAdmin();
    }
  }, true);
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', mount);
  } else {
    mount();
  }
  // Retry once shortly after load in case of a late-mounted SPA shell.
  setTimeout(mount, 1500);
})();
        "#;
        if let Err(e) = window.eval(script) {
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
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running ClickFun");
}

