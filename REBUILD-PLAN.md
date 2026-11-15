# REBUILD PLAN — ClickFun UI identical to HihiBooth (option A)

Status: PENDING APPROVAL — no code/assets committed yet

## Goal
Make the ClickFun UI visually and behaviorally identical to HihiBooth Desktop by
reusing the extracted frontend (Vue 3 + Ant Design Vue + Pinia + vue-router,
Vietnamese UI, Ant Design theme, Quicksand fonts).

## What we already have
- Full extracted frontend: `hihibooth-ui-extracted/` (root: `index.rendered.html` — the
  loading-overlay + license gate DOM as shipped; `assets_*.js/css`)
- IPC surface (from main bundle): print_image, reprint_image, list_print_ready_files,
  start_selfbooth_camera_session, capture_original_photo, apply_camera_control_settings,
  get/set_local_desktop_settings, get_desktop_license_status, install_bundled_digicamcontrol,
  sync jobs, app update, download_to_local, clear_local_capture_data, ...
- Backend hosts: panel.hihibooth.app/api (license/frames/layouts/categories/filters/payment),
  cdn.freehihi.com (media CDN), ipc.localhost (Tauri commands)
- Bridge backend already built: Camera (Canon EDSDK helper / fake), live view MJPEG,
  print DS-RX1 (2x6/4x6 auto), OpenCV filters/overlay, settings store

## Architecture (identical look, our engine)
```
ClickFun.exe (Tauri 2, Rust)
└─ dist/ = extracted HihiBooth frontend (verbatim copy, license gate intact)
     │ fetch: /api/* → configurable (default: your panel if reachable; else stub w/ offline mode)
     │ invoke: ipc.localhost/* → SAME command names, implemented by ClickFun Rust commands
     ▼                                        or delegated to our .NET bridge over HTTP
ClickFun.CameraBridge.exe (existing)   ← camera/print/settings, extended with the IPC contract
```

## Work items
1. **Repo privacy**: stop tracking extracted assets in public repo (`.gitignore: /ui-pack/`);
   ClickFun keeps its private UI pack or switch repo to private. [NEEDS YOUR DECISION]
2. Vendor extracted assets as `ui-pack/` in ClickFun (NOT committed to public repo).
3. Tauri shell loads `ui-pack/index.html` (rename/adjust paths to /ui-pack/assets/…).
4. **IPC adapter** — implement the command surface used by the UI, backed by our bridge:
   - list_available_printers / open_printer_preferences / print_image / reprint_image / print_test_page
   - get/set_local_desktop_settings (JSON in %ProgramData%\ClickFun)
   - start_selfbooth_camera_session / capture_original_photo / live_view_status / apply_camera_control_settings
   - list_print_ready_files / list_available_cameras / clear_local_capture_data / download_to_local
   - get_desktop_license_status (proxy panel if reachable; else "unlicensed" — never fake)
   - install_bundled_digicamcontrol — NOT reimplemented; stub returns "not supported"
   - sync jobs / app update — pass-through stubs reporting "not configured" (offline booth OK)
5. **Panel API adapter**: API base URL via env (`CLICKFUN_API_HOST`), default panel.hihibooth.app |
   when unreachable → offline mode (UI already handles sync-later flows).
6. Keep **license gate** — identical behavior requires panel reachable + licensed machine.
7. UI polish not included — this track is "as extracted", zero visual edits.

## Legal/ethical check (unchanged)
- Assets = your own code (authorized extraction, app is yours)
- No license bypass, no panel credential forgery, no shipping the UI pack in the public repo
- HihiBooth binaries/SDKs never redistributed

## What this does NOT include
- Beauty/skin ML (separate native component in original; our equivalent = existing OpenCV filter path)
- Recap video (needs on-camera movie or capture-card; out of this track)
- Sony/Fuji camera paths (our bridge = Canon EDSDK + HDMI/UVC)
- 100% identical *behavior* for network-dependent features without your panel license

## Verification
- `tauri dev` opens → license-required screen identical to original
- Debug/stub mode: point panel at stub → full capture→print flow works offline
- Print path on DS-RX1 machine: real 2x6 and 4x6 prints
