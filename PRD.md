# PRD — ClickFun (HihiBooth-Rebuild) v1.0

Status: READY FOR APPROVAL — no code generated yet
Source: ANALYSIS.md (REA static analysis of HihiBooth, user-owned)
Date: 2026-11-15

## 1. Product summary
**ClickFun** — Windows photo-booth app: live view from a Canon EOS R-series mirrorless, capture photos/video, apply filter + overlay template, print 2x6" on **DNP DS-RX1**. Fullscreen kiosk touch UI, countdown, sound cues, offline-capable.

## 2. Locked decisions
| Decision | Choice |
|---|---|
| Stack | **Tauri (Rust) UI shell + .NET 8 (WPF/console) camera-print sidecar** |
| Camera | Canon EOS R series, Canon EDSDK (user-owned sample code, C#/C++) |
| Printer | DNP DS-RX1 (2×6" dye-sub, Windows print driver) |
| App name | ClickFun |
| Scope | Photo capture → preview → print; video recording (on-camera); countdown/sound/filters; template overlays |

## 3. Architecture
```
ClickFun.exe  (Tauri 2, Rust)
├─ UI — WebView2 frontend (React + TS), fullscreen kiosk, touch-first
│   home → countdown → capture → preview → retake / print / record video
├─ print pipeline — Rust port of print_dnp.ps1 semantics (winapi PrintDocument via printer.rs:
│   orientation auto-detect from image, rotate-90 fallback, zoom/offset, zero margins, copies w/ 2.5s delay)
└─ IPC (localhost HTTP + WebSocket, 127.0.0.1, token in env)
      │
      ▼
ClickFun.CameraBridge.exe  (.NET 8, self-contained win-x64, no WPF — console/service)
├─ CanonCameraService — Canon EDSDK P/Invoke adapter (Init/Open/LiveView/Capture/Download/Record)
├─ FrameService     — OpenCvSharp: live view frames → latest-frame tap for WebView2 (MJPEG/base64)
├─ VideoService     — EDSDK movie record start/stop + download .MP4 from card
├─ OverlayService   — OpenCvSharp: apply template/overlay + filter (grayscale/sepia/contrast)
└─ Health/Status    — camera battery, SD space, connection state
```

Config & secrets via environment: `CLICKFUN_CAMERA_MODEL`, `CLICKFUN_PRINTER`, `CLICKFUN_BRIDGE_PORT`, `CLICKFUN_BRIDGE_TOKEN`, template dir, etc. No secrets in code or client bundle.

## 4. MVP scope (v0.1)
1. Bridge: Canon EDSDK enumerate/open/live-view (latest frame)/capture/download-to-disk
2. Bridge: EDSDK movie record start/stop + download MP4; frame tap for live view
3. Tauri UI: home → countdown → capture → preview → retake / print; photo strips? (later)
4. Print on DS-RX1: zoom, offsets, copies ×N @2.5 s, orientation auto, 2×6" fit
5. Countdown (1/3/5 s), shutter beep + end-of-countdown chime (own sounds — no HihiBooth assets)
6. Filters: none/grayscale/sepia/vintage (OpenCV LUTs)
7. Template overlays: PNG with alpha, anchored corners/edges + timestamp/logo text
8. Settings via hidden gesture on home (printer, copies, zoom/offsets, countdown, filter default)
9. Settings persisted in `%ProgramData%\ClickFun\settings.json`; photos in `%ProgramData%\ClickFun\photos\YYYY-MM-DD\`

## 5. Later (not v0.1)
GIF capture, cloud gallery/QR share, payment, multi-camera, Fuji/Nikon adapters, auto-update, admin dashboard

## 6. Constraints & legal
- Canon EDSDK license = user's (Canon developer program). SDK binaries stay out of the repo; path via env `CANON_EDSDK_PATH`.
- No HihiBooth code/assets/`XSDK.DAT` reused; print pipeline ported semantically only.
- DNP DS-RX1 via standard Windows driver; no DNP private protocol.
- Offline, single-booth machine, Windows 10/11 x64.

## 7. Success criteria
- Live view ≥5 fps @ ≤1024px pipe; capture→preview ≤2 s
- Print output matches screen preview; settings persist
- Camera unplug/replug → graceful reconnect, no crash
- First print on DS-RX1 passes zoom/offset calibration test
- Zero dependence on original HihiBooth install

## 8. Risks
| Risk | Mitigation |
|---|---|
| Canon EDSDK EOS R live view quality/fps | Develop adapter interface early; DirectShow/UVC capture-card fallback |
| EDSDK property IDs differ per R-model | Probe generically, log unknowns, degrade gracefully |
| DS-RX1 driver ignores orientation | Port rotate-90 workaround (already in ANALYSIS) |
| IPC debugging between Tauri ↔ bridge | Versioned JSON protocol + `/health` + file logs both sides |
| Video download size/time on card | Only download on demand; show progress |

## 9. Open inputs needed
1. Path to your Canon EDSDK sample/wrapper code (to wire into bridge)
2. Exact Canon model (R50/R10/R6…) + lens — affects EDSDK property set
3. Photos output dir & naming preference (default: `%ProgramData%\ClickFun\photos\YYYY-MM-DD\CLICKFUN_yyyyMMdd_HHmmss.jpg`)
4. Overlay templates — do you have PNGs or need generated placeholders?
