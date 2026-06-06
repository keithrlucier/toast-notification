# DC-I1: ONE-TIME ICON GENERATOR — Generates ToastNotification.ico from source PNG.
# The output .ico file is committed to the repo. This script is no longer needed
# unless the brand icon needs regeneration. Last known use: initial project setup.
# Input PNG required: update $sourcePng path before running.

# Generate ToastNotification.ico from the amber bell PNG.
# Produces a multi-resolution ICO (16, 32, 48, 256) with PNG-encoded images.
# Run from repo root:   .\scripts\generate-app-icon.ps1

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repoRoot  = Split-Path -Parent $PSScriptRoot
$sourcePng = Join-Path $repoRoot "Docs\ToastRevival\Design\logo-concepts\toast-icon-amber.png"
$outputIco = Join-Path $repoRoot "src\ToastRevival.Agent\ToastNotification.ico"

if (-not (Test-Path $sourcePng)) { throw "Source PNG not found: $sourcePng" }

$sizes = @(16, 32, 48, 256)

$src = [System.Drawing.Bitmap]::new($sourcePng)

$pngBlobs = @()
foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($src, 0, 0, $sz, $sz)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs += ,$ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}
$src.Dispose()

# Build ICO binary: 6-byte file header + 16-byte directory entry per image + image data.
# PNG embedding in ICO is supported by Windows Vista+ (and all current Windows 10/11).
$count      = $sizes.Count
$dataOffset = 6 + 16 * $count

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# File header
$bw.Write([uint16]0)       # reserved
$bw.Write([uint16]1)       # type = ICO
$bw.Write([uint16]$count)  # image count

# Directory entries
$offset = $dataOffset
for ($i = 0; $i -lt $count; $i++) {
    $sz   = $sizes[$i]
    $blob = $pngBlobs[$i]
    $dimVal = $sz; if ($sz -ge 256) { $dimVal = 0 }; $dim = [byte]$dimVal
    $bw.Write($dim)   # width  (0 encodes 256)
    $bw.Write($dim)   # height (0 encodes 256)
    $bw.Write([byte]0)          # color count (0 = truecolor)
    $bw.Write([byte]0)          # reserved
    $bw.Write([uint16]1)        # planes
    $bw.Write([uint16]32)       # bits per pixel
    $bw.Write([uint32]$blob.Length)
    $bw.Write([uint32]$offset)
    $offset += $blob.Length
}

# Image data
foreach ($blob in $pngBlobs) { $bw.Write($blob) }

$bw.Flush()
$bytes = $ms.ToArray()
$bw.Dispose(); $ms.Dispose()

[System.IO.File]::WriteAllBytes($outputIco, $bytes)
Write-Host "Generated: $outputIco  ($([Math]::Round($bytes.Length / 1KB, 1)) KB, $count sizes: $($sizes -join ', ')px)"
