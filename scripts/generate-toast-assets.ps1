[CmdletBinding()]
param(
    [string] $OutputDir
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

if (-not $OutputDir) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $OutputDir = Join-Path $repoRoot "src\ToastRevival.Agent\Assets"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$brandTeal      = [System.Drawing.Color]::FromArgb(0,   201, 167)
$brandTealDark  = [System.Drawing.Color]::FromArgb(0,   168, 140)
$panelDark      = [System.Drawing.Color]::FromArgb(15,  17,  23)
$panelMid       = [System.Drawing.Color]::FromArgb(26,  29,  39)
$textPrimary    = [System.Drawing.Color]::FromArgb(240, 240, 245)

function New-GradientPng {
    param(
        [int]$Width,
        [int]$Height,
        [System.Drawing.Color]$ColorA,
        [System.Drawing.Color]$ColorB,
        [string]$Path,
        [string]$Wordmark,
        [int]$WordmarkSize = 28
    )

    $bitmap   = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $rect  = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, $ColorA, $ColorB,
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $graphics.FillRectangle($brush, $rect)
    $brush.Dispose()

    if ($Wordmark) {
        $font     = New-Object System.Drawing.Font("Segoe UI", $WordmarkSize, [System.Drawing.FontStyle]::Bold)
        $shadow   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(80, 0, 0, 0))
        $fg       = New-Object System.Drawing.SolidBrush($textPrimary)
        $textSize = $graphics.MeasureString($Wordmark, $font)
        $x        = ($Width  - $textSize.Width)  / 2
        $y        = ($Height - $textSize.Height) / 2
        $graphics.DrawString($Wordmark, $font, $shadow, [single]($x + 1), [single]($y + 1))
        $graphics.DrawString($Wordmark, $font, $fg,     [single]$x,       [single]$y)
        $shadow.Dispose(); $fg.Dispose(); $font.Dispose()
    }

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}

function New-LogoPng {
    param(
        [int]$Size,
        [string]$Path
    )

    $bitmap   = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $bgBrush = New-Object System.Drawing.SolidBrush($brandTeal)
    $graphics.FillRectangle($bgBrush, 0, 0, $Size, $Size)
    $bgBrush.Dispose()

    $fontSize = [Math]::Max(8, [Math]::Floor($Size * 0.55))
    $font     = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold)
    $fg       = New-Object System.Drawing.SolidBrush($panelDark)
    $glyph    = "T"
    $textSize = $graphics.MeasureString($glyph, $font)
    $x        = ($Size - $textSize.Width)  / 2
    $y        = ($Size - $textSize.Height) / 2
    $graphics.DrawString($glyph, $font, $fg, [single]$x, [single]$y)
    $fg.Dispose(); $font.Dispose()

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}

$heroPath   = Join-Path $OutputDir "toast-hero.png"
$logoPath   = Join-Path $OutputDir "toast-logo.png"
$inlinePath = Join-Path $OutputDir "toast-inline.png"

New-GradientPng -Width 364 -Height 180 -ColorA $brandTeal     -ColorB $brandTealDark -Path $heroPath   -Wordmark "Toast Notification" -WordmarkSize 24
New-LogoPng     -Size  48                                                            -Path $logoPath
New-GradientPng -Width 200 -Height 120 -ColorA $panelMid      -ColorB $panelDark     -Path $inlinePath -Wordmark "Action"      -WordmarkSize 18

Write-Host "Generated:"
Write-Host "  $heroPath"
Write-Host "  $logoPath"
Write-Host "  $inlinePath"
