# ClickFun

Independent photo-booth application rebuilt from a clean, owned implementation.
Windows x64 · .NET 8 camera/print bridge + Tauri 2 (Rust) kiosk UI.

**Second PC / fresh machine?** See [`docs/FRESH-MACHINE.md`](docs/FRESH-MACHINE.md) —
exact clone → build → run steps, plus what is intentionally NOT in this repo
and where to transfer it from.

## Layout

```
ClickFun/
├─ src/ClickFun.CameraBridge/   .NET 8 (self-contained win-x64) IPC + camera + print
├─ src/ClickFun.CameraBridge.Tests/  xUnit tests
├─ app/                         Tauri 2 + React/TS kiosk UI (fullscreen)
├─ tools/CanonEdsdkHelper.cs    Camera helper source (Canon EDSDK, user-provided)
├─ PRD.md                       product requirements (approved 2026-11-15)
├─ ANALYSIS.md                  static analysis of the reference app (REA)
└─ README.md
```

## Architecture

```
ClickFun.exe (Tauri, WebView2) ──HTTP + X-ClickFun-Token──▶ 127.0.0.1:8737
                                                            ClickFun.CameraBridge.exe
                                    ├─ camera fake (dev) | Canon EDSDK via helper (prod)
                                    ├─ filter/overlay pipeline (OpenCV)
                                    └─ print pipeline → DNP DS-RX1 (System.Drawing.Printing)
```

## Camera backends

### `CLICKFUN_CAMERA=fake` (default, development)
Synthetic OpenCV frames; full pipeline testable without hardware.

### `CLICKFUN_CAMERA=canon` (production — Canon EOS R)
Talks to the user's **CanonEdsdkHelper** process (JSON-lines over stdin/stdout),
the same helper architecture validated on your R50:

- helper exe resolution, first match wins:
  1. `CLICKFUN_CANON_HELPER` → path to a prebuilt `CanonEdsdkHelper.exe`
  2. `CLICKFUN_EDSDK_DIR` → folder with `EDSDK.dll` (+ `EdsImage.dll`); the bundled
     `tools/CanonEdsdkHelper.cs` is compiled on demand with inbox csc.exe (x86) into
     `%TEMP%\clickfun-canon\`
  3. `canon-edsdk/CanonEdsdkHelper.exe` next to the bridge (production layout)

- Canon EDSDK binaries are **never committed** (see `.gitignore`)
- helper commands implemented: `status` (model/serial/battery), `liveViewStart/Stop/Frame`
  (background pump, ~2.5 fps EVF), `capture` (full-res JPEG to photos dir), `importRecent`
- **Video is NOT implemented** — the helper exposes photo capture only. Adding movie record
  (EDSDK `kEdsCameraCommand_PressShutterButton` video mode + `EdsDownload` of the .MP4)
  is the next milestone; `StartMovieAsync` returns false until then
- Reaping note: every AF press in the helper releases `ShutterButton_OFF` in `finally`
  (your R50 fix for the 0x81 busy hang) — keep this invariant if the helper is edited

### Parity with your existing booth
`vue-photobooth` (Electron + Vue) already ships this exact helper integration
(`canonEdsdk.ts`). ClickFun reuses the same helper source, not a rewrite.

### REA analysis of the helper (evidence)
`canon-test-final-x86/CanonEdsdkHelper.exe` (sha256 `96ffc6c4…d7c28`):
managed .NET Framework (v4 CLR), x86, IL-only, 28 P/Invokes — all into `EDSDK.dll`
(`EdsInitializeSDK`…`EdsDownloadEvfImage`), no other native deps. Inspected with
REA MCP (`inspect_managed_artifact` + `inspect_managed_native_boundaries`).

## Run (development)

```powershell
# 1) Bridge — fake camera
cd src\ClickFun.CameraBridge
$env:CLICKFUN_CAMERA = "fake"           # or "canon"
$env:CLICKFUN_BRIDGE_TOKEN = "smoke-test-token"
dotnet run

#    or against the real camera helper:
$env:CLICKFUN_CAMERA = "canon"
$env:CLICKFUN_EDSDK_DIR = "C:\path\to\folder\with\EDSDK.dll"   # e.g. your canon-test-final-x86 dir
dotnet run

# 2) UI
cd ..\..\..\app
npm install
npx tauri dev                            # needs Rust (see below)
# vite-only preview: npm run dev  → http://localhost:1420
```

## Toolchain notes (Windows, no admin available)

- Rust via **rustup**; default toolchain is `stable-x86_64-pc-windows-gnullvm`
  (pairs with **LLVM-MinGW**, installed at
  `…\WinGet\Packages\MartinStorsjo.LLVM-MinGW.UCRT_…\llvm-mingw-20261020-ucrt-x86_64\bin` —
  put it on PATH for `cargo`)
- `stable-x86_64-pc-windows-gnu` was tried first; failed on missing `libgcc_eh` (LLVM-MinGW
  ships compiler-rt/libunwind only). `msvc` target needs Build Tools (admin) — not used
- `crate-type = ["rlib"]` (not cdylib) to avoid lld "too many exported symbols"
- `cargo build` verified; `clickfun-app.exe` links

If you later install VS Build Tools (elevated), `rustup default stable-x86_64-pc-windows-msvc`
is the preferred target.

## Tests

```powershell
dotnet test        # 13/13 passing (FitGeometry, PaperDetect, settings round-trip, filter decode)
```

## Live view & paper detection

- `CLICKFUN_LIVEVIEW=auto|hdmi|edsdk` — live view source (default `auto`: HDMI/UVC capture card if one opens (DSHOW indices 0-3), else Canon EVF). Full-res capture is always USB EDSDK
- HDMI/UVC live view endpoints (same contract as the original booth): `/stream.mjpg` (multipart MJPEG), `/frame.jpg`, `/liveview/mode` — token via header or `?token=` for `<img>`
- `/print/layouts` — detected 2x6/4x6 sizes on the configured printer (dimension-matched, locale/firmware independent)
- `/print` accepts `paperLayout: "auto"|"2x6"|"4x6"` (auto matches image aspect to the sizes the DS-RX1 driver exposes)

## Notable implementation decisions

1. **Print pipeline lives in the .NET bridge** (System.Drawing.Printing), not in Rust —
   same semantics as the original booth: zero margins, orientation auto-detect from the
   image, rotate-90 fallback when the driver ignores Landscape, fit-and-center with
   `zoom` (0.98 default)/`offsetX`/`offsetY`, 2.5 s between copies for the DS-RX1.
   `FitGeometry` is pure and unit-tested.
2. **Live view transport = poll-based HTTP JPEG** (`/frame/latest`). Simple, debuggable.
   Move to WebSocket if frame pacing becomes an issue.
3. **Canon helper = x86 .NET Framework process** spawned by the x64 bridge — chosen over
   in-process EDSDK P/Invoke because the driver stack is 32-bit and hosted best isolated
   (your production pattern). The bridge is 64-bit; the helper is 32-bit.
4. **XSDK.DAT / Sony binaries never used.** EDSDK binaries never committed.

## Pending inputs / milestones

- [ ] Smoke test against the real camera (helper connect + live view, no shutter)
- [ ] Video recording command in the helper (EDSDK movie mode) if video is in scope
- [ ] Filter picker + overlay toggle in UI (backend filter/overlay endpoints already exist)
- [ ] `npx tauri build` once installer bundling is wanted (needs WebView2 evergreen runtime on target)

## Security

- Bridge binds `127.0.0.1` only; every endpoint except `/health` requires
  `X-ClickFun-Token` (`CLICKFUN_BRIDGE_TOKEN`). Dev default is `clickfun-dev-token`
  with a loud warning — set a strong token for the booth.
- Secrets only via env vars; nothing in the repo.

## Logs & data

- Logs: `%ProgramData%\ClickFun\logs\bridge-YYYYMMDD.log`
- Settings: `%ProgramData%\ClickFun\settings.json`
- Captures: `%ProgramData%\ClickFun\photos\YYYY-MM-DD\`

## Local admin (`/admin/`)

A self-contained local admin UI (Vue 3 + Ant Design Vue, fully own code — no
third-party product assets) manages the booth without any cloud dependency.
It is served by the bridge at `http://127.0.0.1:8737/admin/` and talks to the
bridge over token-gated `/admin/*` endpoints.

### In-app entry point (kiosk)

The Tauri booth window itself opens the admin — no browser needed:

- Press **Ctrl+Shift+A** inside the booth app, **or**
- Tap the small **gear button** in the bottom-left corner (subtle by default,
  hover/focus brightens it).

Both invoke the `open_admin` Tauri command, which opens (or focuses) a
dedicated **ClickFun Admin** window (external URL `http://127.0.0.1:8737/admin/`,
bordered, 1280×800). The booth UI bundle is left untouched — the entry points
are injected at runtime by the Tauri shell, so they survive UI rebuilds.

Data layer (owned by the bridge, `AdminStore`):

- `%ProgramData%\ClickFun\admin\app-config-cache.json` — layouts / frames / categories /
  paymentSettings / filters used by the booth UI (read by the Tauri `get_cached_app_config`
  adapter). Seeded on first run from a local offline copy if present.
- `%ProgramData%\ClickFun\admin\frames\{id}.{png,jpg,webp}` — uploaded frame images.

Features:

- Dashboard (frames/layouts/categories, camera backend, printer availability, version)
- Frame library: upload (PNG/JPG/WebP), rename, shape (square/grid/strip), toggle active,
  delete, hole-count preview, search + shape filter
- Layouts: create/edit/delete (name, shape, render mode, photos per strip, price, active)
- Categories: create/delete
- Booth settings: countdown, copies, print offsets/zoom, default filter, paper layout,
  photos root — written through the bridge `/settings`
- Camera & printer page: connect/disconnect/test capture, DS-RX1 driver + paper detection
- Logs viewer (today's bridge log tail)

Access control: `/admin` (HTML/JS/CSS/frame images) is served without a token so a
keyboard/touch booth can reach it; every `/admin/*` API call requires
`X-ClickFun-Token` — the UI asks for it once and stores it in `localStorage`.
Set `CLICKFUN_BRIDGE_TOKEN` to a strong value on the booth machine.

Build and run:

```
cd app
npm run build:admin          # builds app/admin/dist (served automatically by the bridge)
cd ../src/ClickFun.CameraBridge && dotnet run
```

The bridge resolves the admin bundle via `CLICKFUN_ADMIN_DIST` or by walking up
from its working directory to `app/admin/dist`.

Single-machine note: this build is intentionally self-contained — licensing/activation
is disabled (owner request), the booth boots straight into `#/layout-selection`, and
the three `get_cached_app_config` / `save_cached_app_config` /
`get_desktop_license_status` Tauri commands are backed by the local cache file above.
