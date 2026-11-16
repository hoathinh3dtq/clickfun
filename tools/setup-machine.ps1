# Copy the two private pieces onto THIS machine from a USB stick / private share.
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\setup-machine.ps1 -UiPackSource "E:\clickfun-private-files\ui-pack" -EdsdkSource "E:\clickfun-private-files\canon-edsdk"
#
# The script only copies files within your own machines. It does NOT download
# anything. UI pack + Canon EDSDK stay on your private media (USB/LAN share).

param(
  [Parameter(Mandatory = $true)][string]$UiPackSource,
  [Parameter(Mandatory = $false)][string]$EdsdkSource
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$uiDest = Join-Path $repoRoot 'ui-pack'

if (-not (Test-Path (Join-Path $UiPackSource 'assets'))) {
  Write-Error "ui-pack source must contain an 'assets' folder (got '$UiPackSource')."
}

Write-Host "==> Copying ui-pack -> $uiDest"
New-Item -ItemType Directory -Force $uiDest | Out-Null
Copy-Item (Join-Path $UiPackSource 'assets') (Join-Path $uiDest 'assets') -Recurse -Force
Copy-Item (Join-Path $UiPackSource 'index.html') (Join-Path $uiDest 'index.html') -Force
if (Test-Path (Join-Path $UiPackSource 'favicon.ico')) {
  Copy-Item (Join-Path $UiPackSource 'favicon.ico') (Join-Path $uiDest 'favicon.ico') -Force
}

if ($EdsdkSource) {
  if (-not (Test-Path (Join-Path $EdsdkSource 'EDSDK.dll'))) {
    Write-Error "canon-edsdk source must contain EDSDK.dll (got '$EdsdkSource')."
  }
  $sdkDest = Join-Path $repoRoot 'tools\canon-edsdk'
  Write-Host "==> Copying canon-edsdk -> $sdkDest (gitignored)"
  New-Item -ItemType Directory -Force $sdkDest | Out-Null
  Copy-Item (Join-Path $EdsdkSource '*') $sdkDest -Recurse -Force
  [Environment]::SetEnvironmentVariable('CLICKFUN_EDSDK_DIR', $sdkDest, 'User')
  $helper = Join-Path $sdkDest 'CanonEdsdkHelper.exe'
  if (Test-Path $helper) {
    [Environment]::SetEnvironmentVariable('CLICKFUN_CANON_HELPER', $helper, 'User')
  }
  Write-Host "    Set user env vars CLICKFUN_EDSDK_DIR (+CLICKFUN_CANON_HELPER). Open a NEW terminal to pick them up."
}

Write-Host ""
Write-Host "Done. Next steps:"
Write-Host "  cd src\ClickFun.CameraBridge.Tests; dotnet test"
Write-Host "  cd ..\ClickFun.CameraBridge; dotnet build"
Write-Host "  cd ..\..\app; npm install --legacy-peer-deps; npm run build:admin"
Write-Host "  # run: dotnet run (bridge) then cargo run (booth), or npx tauri build"
