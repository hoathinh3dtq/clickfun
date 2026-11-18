# Rewrites frame image references in app-config-cache.json to local
# %ProgramData%\ClickFun\admin\frames\{id}{ext} copies, and stages a
# self-contained booth seed folder for USB transfer to fresh machines.
# Writes UTF-8 WITHOUT BOM (serde_json rejects BOM).
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\stage-booth-admin-cache.ps1 [-Stage] <target-dir>
param(
    [string]$Stage = ""
)

$ErrorActionPreference = 'Stop'
$adminRoot = Join-Path $env:ProgramData 'ClickFun\admin'
$cachePath = Join-Path $adminRoot 'app-config-cache.json'
$framesDir = Join-Path $adminRoot 'frames'
New-Item -ItemType Directory -Force -Path $framesDir | Out-Null

Copy-Item $cachePath "$cachePath.bak" -Force

$json = Get-Content $cachePath -Raw | ConvertFrom-Json

$copied = 0; $missing = @()
foreach ($shapeProp in @($json.framesByShape.PSObject.Properties)) {
    foreach ($frame in @($shapeProp.Value)) {
        $src = $null
        foreach ($k in @('url', 'path')) {
            $candidate = $frame.$k
            if ($candidate -and (Test-Path -LiteralPath $candidate)) { $src = $candidate; break }
        }
        if (-not $src) { if ($frame.url) { $missing += $frame.url }; continue }
        $ext = [System.IO.Path]::GetExtension($src)
        if (-not $ext) { $ext = '.png' }
        $destName = "$($frame.id)$ext"
        $dest = Join-Path $framesDir $destName
        if (-not (Test-Path -LiteralPath $dest) -or ((Get-Item -LiteralPath $dest).Length -ne (Get-Item -LiteralPath $src).Length)) {
            Copy-Item -LiteralPath $src -Destination $dest -Force
            $copied++
        }
        $frame.path = $dest
        $frame.url = $dest
    }
}

$out = $json | ConvertTo-Json -Depth 30
[System.IO.File]::WriteAllText($cachePath, $out, (New-Object System.Text.UTF8Encoding($false)))
$first = [System.IO.File]::ReadAllBytes($cachePath)[0..2] -join ' '
Write-Host "Rewrote cache (BOM check bytes: $first) -> self-contained copies in $framesDir (copied $copied image(s))"
if ($missing.Count) { Write-Host "WARNING missing source images (left as-is):"; $missing | ForEach-Object { Write-Host "  $_" } }

if ($Stage) {
    $stageAdmin = Join-Path $Stage 'admin'
    $stageFrames = Join-Path $stageAdmin 'frames'
    New-Item -ItemType Directory -Force -Path $stageFrames | Out-Null
    Copy-Item $cachePath (Join-Path $stageAdmin 'app-config-cache.json') -Force
    Copy-Item (Join-Path $framesDir '*') $stageFrames -Recurse -Force
    Write-Host "Staged self-contained admin seed -> $stageAdmin"
}
