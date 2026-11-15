# HihiBooth — Static Analysis Report

Target: `C:\Program Files\HihiBooth` (user-owned / authorized analysis)
Date: 2026-11-15 · Tooling: REA MCP 6.9.0 (opencode registration healthy) + file reads
Note: `hihibooth.exe` native code was NOT decompiled (no Ghidra configured: `GHIDRA_INSTALL_DIR` unset). Native findings below are from PE inspection + imports. `HihiCamBridge.dll` is .NET IL-only and fully metadata-inspectable.

## Artifacts

| File | Size | Type | Verdict |
|------|------|------|---------|
| `hihibooth.exe` | 15,026,176 B | PE amd64, Rust (rustc commit hash inferred in rodata), NOT cargo-auditable (no `.dep-v0`) | Main GUI app — likely Tauri-style (WebView2 loaded dynamically; user32/touch/RawInput/monitor imports; fullscreen kiosk UI) |
| `HihiBooth.rar` | 222 MB | RAR archive | Full installer (runtime + Sony SDK + drivers) — not opened |
| `sidecars/hihicam-bridge-bin/HihiCamBridge.dll` | 365,568 B | .NET 8 (v8.0, win-x64), IL-only, CIL, 305 types / 1918 methods | Camera engine sidecar, self-contained WPF app (`HihiCamBridge.exe`) |
| `HihiCamSonyAdapter.dll` | 111,104 B | Native PE (no .NET metadata) | Custom flat-C shim over Sony SDK; exports `HihiCamSony_*` |
| `HihiCamBridge.pdb` | 105,092 B | PDB | Debug symbols present (useful for future decompilation) |
| `EDSDK.dll`, `EdsImage.dll`, `SonyDriver/` | — | Sony EDSDK (proprietary) | Sony camera control SDK — license-bound, do not copy/redistribute |
| `monitor_protocol.dll` (8.4 MB) + `monitor_protocol_pf.dll` | — | Native | Sony remote/monitor protocol stack (FF0000API…FF0022API, FTLPTP, Cr_Core) — proprietary |
| `FF0000API.dll`…`FF0022API.dll`, `FTLPTP*.dll`, `Cr_Core.dll`, `XAPI.dll`, `XSDK.DAT` | — | Native / opaque data | Vendor SDK pieces; XSDK.DAT = 385 B high-entropy blob (treated as license/activation material — NOT analyzed, NOT reused) |
| `OpenCvSharp*`, `opencv_videoio_ffmpeg490_64.dll` | — | OpenCV 4.9 | Frame processing |
| `DirectShowLib.dll` | 290 KB | .NET DirectShow | Webcam/capture fallback path |
| `System.*` / `Microsoft.*` / `hostfxr.dll` | — | .NET 8.0.25 self-contained runtime pack | Bridge runs without system .NET install |
| `resources/print_dnp.ps1` | 4.5 KB | PowerShell | DNP dye-sub print pipeline (see below) |
| `beep.mp3` | 16 KB | Asset | UI sound |

SHA-256 (hihibooth.exe): `71f9ae34fd8cb3062f76c98585190fa1a3bff0cc4712bd3d24312b1ec5ea1123`
SHA-256 (HihiCamBridge.dll): `a893789ef1f3a65187405f3491b8aebf2dde078d7919c48d791f838b64c09c4a`

## Evidence IDs (REA)

- rust scan (summary): `ev_c671a12a879546f671629c348f5e4bdb8a490776b7a2240eb653fea3a1386e91` (parent `ev_29d55d28…`)
- PE imports: `ev_0771bb96afb0edf3a1c724b369b61e576aa424a7bb67cffc12b3a064691b1e70`
- managed artifact: `ev_6ab5bb6313f52bd371d04d9ac82263e96540f72fde9a4a9373e10d36ab5ab3c8`
- managed members: `ev_3d39105dcc66d5ca15538c6e7ad9aadceeb069983b1ab015162f5546b0b94fc3`
- native boundaries: `ev_bccd9ae61242b2b6149d144d73b74d7ba88f8896caf708228effc42e6638cb4f`

## Architecture (observed)

```
┌─────────────────────────────────────────────┐
│ hihibooth.exe (Rust, windowed fullscreen)   │  kiosk UI, touch, countdown, preview
│   ├─ spawns/controls → print_dnp.ps1        │  DNP printing via System.Drawing.PrintDocument
│   └─ IPC/launch → HihiCamBridge.exe         │  camera sidecar (localhost / process)
└──────────────┬──────────────────────────────┘
               │  (mechanism not fully decoded — see Limitations)
┌──────────────▼──────────────────────────────┐
│ HihiCamBridge (.NET 8 WPF, self-contained)  │
│   ├─ Sony EDSDK path (P/Invoke EDSDK.dll)   │  Eds* live view (EVF), capture, property RW
│   ├─ Native adapter HihiCamSonyAdapter.dll  │  flat-C exports HihiCamSony_* (Initialize/
│   │     wraps monitor_protocol.dll          │  Enumerate/OpenSession/IsConnected/
│   │                                          │  GetLatestFrame/Capture/GetSettingOptions/
│   │                                          │  SetSetting/PulseKeepAlive/CloseSession)
│   ├─ DirectShow (DirectShowLib)             │  UVC/DirectShow capture fallback
│   ├─ Media Foundation (mfplat/mfreadwrite)  │  MFCreateSinkWriterFromURL → video recording
│   ├─ OpenCvSharp (OpenCV)                   │  frame processing
│   └─ winmm mciSendString, user32, ole32     │  audio beep, window mgmt, COM
└─────────────────────────────────────────────┘
```

## print_dnp.ps1 behavior (read directly)

- Args: `-imagePath -printerName [-quantity N] [-zoom 0.98] [-offsetX N] [-offsetY N]`
- Normalizes `\\?\` / `\??\` / `\\?\UNC\` prefixes → plain paths
- Loads `System.Drawing`; landscape auto-detected from image; forces page orientation to match (rotates image 90° if driver ignores Landscape)
- Fit-to-page (min of ratios) preserving aspect, multiplied by `zoom`, centered + pixel offsets
- `PrintDocument`, zero margins, `OriginAtMargins=false`, high-quality bicubic; prints `quantity` copies, 2.5 s between copies for dye-sub heads

## Sony EDSDK surface P/Invoked (partial)

EdsInitializeSDK/TerminateSDK, GetCameraList, GetChildCount/AtIndex, GetDeviceInfo,
OpenSession/CloseSession, EdsRelease, SetObjectEventHandler, SendCommand,
Get/SetPropertyData, GetPropertyDesc, SetCapacity, GetDirectoryItemInfo,
CreateFileStream (ANSI, EdsCreateEvfImageRef → EdsDownloadEvfImage → EdsGetPointer/GetLength),
EdsDownload/Complete/Cancel/GetEvent, MemoryStream.

## Limitations / unknowns

1. Ghidra not installed → native Rust code and `HihiCamSonyAdapter.dll` not decompiled; IPC between hihibooth.exe and the bridge (pipe/socket/named-mutex? XML/JSON/binary?) not confirmed.
2. XSDK.DAT treated as opaque license/activation data — analysis deliberately skipped; nothing reused.
3. Sony EDSDK / monitor_protocol / Cr_Core are proprietary, license-bound: the rebuild must use the user's own legally-obtained Sony SDK, never these files.
4. `HihiCamBridge.pdb` present — future deep decompile possible if ilspycmd/decompiler is installed.
5. Feature scope (sessions, templates, timers, cloud sync, etc.) not yet mapped — needs either runtime observation (with user approval) or UI/string inspection.
