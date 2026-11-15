# ClickFun

Independent photo-booth application rebuilt from a clean, owned implementation.
Windows x64 · .NET 8 camera/print bridge + Tauri 2 (Rust) kiosk UI.

## Layout

```
ClickFun/
├─ src/ClickFun.CameraBridge/   .NET 8 (self-contained win-x64) IPC + camera + print
├─ src/ClickFun.CameraBridge.Tests/  xUnit tests
├─ app/                         Tauri 2 + React/TS kiosk UI (fullscreen)
├─ PRD.md                       product requirements (approved 2026-11-15)
├─ ANALYSIS.md                  static analysis of the reference app (REA)
└─ README.md
```

## Architecture

```
ClickFun.exe (Tauri, WebView2) ──HTTP + X-ClickFun-Token──▶ 127.0.0.1:8737
                                                            ClickFun.CameraBridge.exe
                                    ├─ camera Fake (dev) | Canon EDSDK (prod)
                                    ├─ filter/overlay pipeline (OpenCV)
                                    └─ print pipeline → DNP DS-RX1 (System.Drawing.Printing)
```

## Run (development)

```powershell
# 1) Bridge (fake camera — works without hardware)
cd src\ClickFun.CameraBridge
$env:CLICKFUN_CAMERA = "fake"
$env:CLICKFUN_BRIDGE_TOKEN = "smoke-test-token"
dotnet run

# 2) UI (needs Rust toolchain — not installed yet)
cd ..\..\..\app
npm install
npm run dev        # vite only (port 1420)
# or, once Rust is installed: npx tauri dev
```

Smoke endpoints (token required except /health):

```powershell
curl http://127.0.0.1:8737/health
curl -H "X-ClickFun-Token: $env:CLICKFUN_BRIDGE_TOKEN" -X POST http://127.0.0.1:8737/camera/connect
curl -H "X-ClickFun-Token: $env:CLICKFUN_BRIDGE_TOKEN" http://127.0.0.1:8737/frame/latest -o frame.jpg
curl -H "X-ClickFun-Token: $env:CLICKFUN_BRIDGE_TOKEN" -X POST http://127.0.0.1:8737/capture
```

## Camera

- `CLICKFUN_CAMERA=fake` (default): synthetic frames — full pipeline works without hardware.
- `CLICKFUN_CAMERA=canon`: Canon EOS R via Canon EDSDK. **Not yet wired** — set `CANON_EDSDK_PATH`
  (folder containing EDSDK.dll), then implement `src/ClickFun.CameraBridge/Camera/CanonEdsdkCameraService.cs`
  using your Canon EDSDK sample code. Canon EDSDK binaries are never committed.

## Tests

```powershell
dotnet test
```

## Notable implementation decisions

1. **Print pipeline lives in the .NET bridge** (System.Drawing.Printing), not in Rust.
   The original booth's print logic proved out on this exact stack; raw Win32 GDI+ from
   Rust would be a large, error-prone detour. Semantics are identical: zero margins,
   orientation auto-detect from the image, rotate-90 fallback when the driver ignores
   Landscape, fit-and-center with `zoom`/`offsetX`/`offsetY`, 2.5 s between copies
   for the DS-RX1 dye-sub head. `zoom=0.98` default matches the observed behavior.
2. **Frontend is a skeleton**, not feature-complete: capture loop, countdown, preview,
   print, settings modal (hidden via double-click on brand mark). Video recording UI and
   overlay/filter toggles land next.
3. **Live view transport is poll-based HTTP JPEG** (`/frame/latest`, 250 ms interval).
   Simple and debuggable; move to WebSocket if frame pacing becomes an issue.
4. **XSDK.DAT / Sony binaries / Canon EDSDK binaries are never used or redistributed.**

## Pending inputs (blocking Canon path)

- Your Canon EDSDK sample code (path/contents) — to wire `CanonEdsdkCameraService`
- Exact Canon model (R50 / R10 / R6…) — EDSDK property IDs differ
- Final overlay template PNGs (placeholders will be generated until then)

## Security

- Bridge binds `127.0.0.1` only; every endpoint except `/health` requires
  `X-ClickFun-Token` (`CLICKFUN_BRIDGE_TOKEN`). Development default is
  `clickfun-dev-token` with a loud warning — set a strong token for the booth.
- No secrets in the repo; config via environment variables only.

## Logs & data

- Logs: `%ProgramData%\ClickFun\logs\bridge-YYYYMMDD.log`
- Settings: `%ProgramData%\ClickFun\settings.json`
- Captures: `%ProgramData%\ClickFun\photos\YYYY-MM-DD\`
