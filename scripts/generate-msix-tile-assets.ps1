[CmdletBinding()]
param(
    [string] $OutputDir
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

if (-not $OutputDir) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $OutputDir = Join-Path $repoRoot "src\ToastRevival.Agent\Images"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$brandTeal     = [System.Drawing.Color]::FromArgb(0,   201, 167)
$brandTealDark = [System.Drawing.Color]::FromArgb(0,   168, 140)
$panelDark     = [System.Drawing.Color]::FromArgb(15,  17,  23)
$textPrimary   = [System.Drawing.Color]::FromArgb(240, 240, 245)

function New-SquareTile {
    param(
        [int]$Size,
        [string]$Path,
        [string]$Glyph = "T"
    )

    $bitmap   = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $bgBrush = New-Object System.Drawing.SolidBrush($brandTeal)
    $graphics.FillRectangle($bgBrush, 0, 0, $Size, $Size)
    $bgBrush.Dispose()

    $fontSize = [Math]::Max(8, [Math]::Floor($Size * 0.55))
    $font     = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold)
    $fg       = New-Object System.Drawing.SolidBrush($panelDark)
    $textSize = $graphics.MeasureString($Glyph, $font)
    $x        = ($Size - $textSize.Width)  / 2
    $y        = ($Size - $textSize.Height) / 2
    $graphics.DrawString($Glyph, $font, $fg, [single]$x, [single]$y)
    $fg.Dispose(); $font.Dispose()

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}

function New-WideTile {
    param(
        [int]$Width,
        [int]$Height,
        [string]$Path,
        [string]$Wordmark,
        [int]$WordmarkSize = 24
    )

    $bitmap   = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $rect  = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, $brandTeal, $brandTealDark,
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $graphics.FillRectangle($brush, $rect)
    $brush.Dispose()

    $font     = New-Object System.Drawing.Font("Segoe UI", $WordmarkSize, [System.Drawing.FontStyle]::Bold)
    $shadow   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(80, 0, 0, 0))
    $fg       = New-Object System.Drawing.SolidBrush($textPrimary)
    $textSize = $graphics.MeasureString($Wordmark, $font)
    $x        = ($Width  - $textSize.Width)  / 2
    $y        = ($Height - $textSize.Height) / 2
    $graphics.DrawString($Wordmark, $font, $shadow, [single]($x + 1), [single]($y + 1))
    $graphics.DrawString($Wordmark, $font, $fg,     [single]$x,       [single]$y)
    $shadow.Dispose(); $fg.Dispose(); $font.Dispose()

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}

$square44   = Join-Path $OutputDir "Square44x44Logo.png"
$square150  = Join-Path $OutputDir "Square150x150Logo.png"
$wide310    = Join-Path $OutputDir "Wide310x150Logo.png"
$store      = Join-Path $OutputDir "StoreLogo.png"

New-SquareTile -Size 44  -Path $square44
New-SquareTile -Size 150 -Path $square150
New-SquareTile -Size 50  -Path $store
New-WideTile   -Width 310 -Height 150 -Path $wide310 -Wordmark "Toast Notification" -WordmarkSize 22

Write-Host "Generated MSIX tile assets:"
Write-Host "  $square44"
Write-Host "  $square150"
Write-Host "  $wide310"
Write-Host "  $store"
