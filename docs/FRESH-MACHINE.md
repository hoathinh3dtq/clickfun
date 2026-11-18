# ClickFun — Fresh machine setup (admin + booth app)

Verified on 2026-11-16 by cloning this repo into a clean directory and rebuilding:

- `dotnet test` — 13/13 pass
- `npm install && npm run build:admin` — builds
- `cargo check` — fails until `ui-pack/` exists (by design, see below)

## What is in this repo (public)

| Component | Path | What it is |
|-----------|------|------------|
| Camera/print/live-view bridge | `src/ClickFun.CameraBridge` | .NET 8 HTTP service, token-gated, port 8737 |
| Tauri shell (booth window + in-app admin) | `app/src-tauri` | Rust, `com.clickfun.booth` |
| Local admin (Vue 3 + Ant Design Vue) | `app/admin` | Original UI, served at `/admin/` by the bridge |
| Tests | `src/ClickFun.CameraBridge.Tests` | xUnit |
| Analysis & planning docs | `PRD.md`, `ANALYSIS.md`, `REBUILD-PLAN.md` | REA analysis of the original app |

## What is NOT in this repo (and why)

1. **`ui-pack/`** — the booth kiosk UI, extracted from the original HiHiBooth
   app you own. It is gitignored and must be **transferred privately**
   (USB stick, LAN share, private SSH). Do not upload it anywhere.
   On a machine with the original app installed, the same files can also be
   re-extracted from its WebView2 asset cache.
2. **Canon EDSDK** (`EDSDK.dll`, `EdsImage.dll`, `CanonEdsdkHelper.exe`) — Canon's
   SDK; never in this repo. Set `CLICKFUN_EDSDK_DIR` (and `CLICKFUN_CANON_HELPER`
   for the prebuilt helper) on the booth PC.
3. **Photos, frames, license/keys** — `%ProgramData%\ClickFun` on each machine.
4. **`booth-admin-cache/admin/`** — the seeded offline cache (frame list with
   holes metadata, layouts, categories, payment/print paths) plus the frame
   images, with every `path`/`url` rewritten to machine-independent
   `%ProgramData%\ClickFun\admin\frames\{id}.png`. Install it with
   `setup-machine.ps1 -BoothAdminCacheSource "...\booth-admin-cache"` — without it
   the booth on a fresh PC boots with an empty frame grid (the original app's
   legacy cache lived under `%AppData%\com.hihi.boothv2`, which does not exist on
   a fresh machine). To re-stage this folder on a machine that has
   `%ProgramData%\ClickFun\admin` populated, run
   `tools\stage-booth-admin-cache.ps1 -Stage <target-dir>`.
5. EDSDK full build needs `CLICKFUN_EDSDK_DIR` pointing to a folder with
   `EDSDK.dll` + `EdsImage.dll` (the bridge compiles `tools/CanonEdsdkHelper.cs`
   on demand with the inbox x86 csc.exe, or use a prebuilt `CanonEdsdkHelper.exe`).

## 3-machine plan (home PC → booth PC with Canon + DS-RX1 → work PC)

Two pieces exist **only on this machine** and cannot be pushed publicly:

1. `ui-pack/` — the extracted kiosk UI (HiHiBooth's product, gitignored on purpose)
2. `canon-edsdk/` — Canon's SDK binaries (`EDSDK.dll`, `EdsImage.dll`, helper)

Plus a third self-contained seed for fresh PCs:

3. `booth-admin-cache/` — `%ProgramData%\ClickFun\admin` seed (offline cache +
   frame images, machine-independent absolute paths). Without it a fresh booth
   PC boots with an empty frame grid.

Move them privately once with a USB stick / private LAN share (never GitHub):

```
[home PC]  copy C:\Users\Admin\Music\HihiBooth-Rebuild\ClickFun\ui-pack      -> USB:\clickfun-private-files\ui-pack
           copy C:\Users\Admin\AppData\Roaming\camrent-booth-app\canon-edsdk    -> USB:\clickfun-private-files\canon-edsdk
           (clickfun-private.zip already contains ui-pack\, canon-edsdk\ and booth-admin-cache\)
[booth PC] tools\setup-machine.ps1 -UiPackSource "E:\clickfun-private-files\ui-pack" -EdsdkSource "E:\clickfun-private-files\canon-edsdk" -BoothAdminCacheSource "E:\clickfun-private-files\booth-admin-cache"
[work PC]  (coding only) same script without -EdsdkSource if you don't need the camera
```

Everything else is already on GitHub (`d9f83d2`), verified by a clean clone:
`dotnet test` 13/13, `npm run build:admin` OK. The `CLICKFUN_EDSDK_DIR` /
`CLICKFUN_CANON_HELPER` env vars are set by the same script for the booth PC.

## Prerequisites (Windows)

1. **Git**
2. **.NET 8 SDK** (8.0.4xx)
3. **Node 24 LTS** + npm
4. **Rust** — stable toolchain; this machine builds with `stable-x86_64-pc-windows-gnullvm`
   + LLVM-MinGW (no Visual Studio/MSVC available). Any normal `stable` toolchain also works
   if you have MSVC Build Tools (C++ workload) + on-machine WebView2.
5. WebView2 runtime (already present on Win10/11)

## Setup steps

```powershell
git clone https://github.com/hoathinh3dtq/clickfun
cd clickfun

# 1) Booth kiosk UI — copy from the source machine (USB/LAN)
Copy-Item -Recurse D:\path\to\ui-pack .\ui-pack   # 5 MB, gitignored

# 2) Bridge: build + tests
cd src\ClickFun.CameraBridge.Tests
dotnet test                          # expect 13 passed
cd ..\ClickFun.CameraBridge
dotnet build

# 3) Admin: build the local admin UI (output goes to app\admin\dist, gitignored)
cd ..\..\app
npm install --legacy-peer-deps
npm run build:admin

# 4) Run the bridge (fake camera; set canon vars for the real booth)
$env:CLICKFUN_CAMERA="fake"          # or "canon"
$env:CLICKFUN_BRIDGE_PORT="8737"
$env:CLICKFUN_BRIDGE_TOKEN="change-me-on-the-booth"
.\bin\Debug\net8.0-windows\win-x64\ClickFun.CameraBridge.exe

# 5) Run the booth app (debug)
cd ..\src-tauri
cargo run
```

## Real-camera environment (booth PC)

```powershell
set CLICKFUN_CAMERA=canon
set CLICKFUN_EDSDK_DIR=D:\SDK\canon-edsdk          # EDSDK.dll + EdsImage.dll
set CLICKFUN_CANON_HELPER=D:\SDK\canon-edsdk\CanonEdsdkHelper.exe
```

Live view is EDSDK-over-USB if no HDMI capture card is present
(`CLICKFUN_LIVEVIEW=hdmi` when a UVC capture card is installed).

## Verified build status (2026-11-16)

- `dotnet test` — 13/13 pass
- `npm install && npm run build:admin` — OK
- `cargo check` — only missing `ui-pack/` (expected until step 1)
- Booth end-to-end (fake camera): boots to frame selection, touch-to-capture flow works
- Local admin end-to-end: frame CRUD, sessions grid, print log, stats, filters, license info

## Dist/exe layout per machine

- `app/src-tauri/target/debug/clickfun-app.exe` — booth app (use `npx tauri build` for installer)
- `src/ClickFun.CameraBridge/bin/.../ClickFun.CameraBridge.exe` — bridge
- `app/admin/dist/` — the admin UI (no separate file to distribute; the bridge serves it)

## Notes

- Repo is public → `ui-pack/`, `EDSDK**`, `*.jsonl` logs, photos are gitignored.
- `git clone` on another PC + `ui-pack` copy is all that is needed; there is no
  private registry or hosted dependency.
