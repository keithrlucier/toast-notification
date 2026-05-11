[CmdletBinding()]
param(
    [string] $OutputDir
)

# M9.C -- Diana's production tile spec.
#
# Brand expression matches the marketing site (https://toastnotification.com):
#   Background  #0A0F1A   near-black panel
#   Accent      #F59E0B   brand amber
#   Wordmark    #F0F0F5   warm white, "Toast Notification" (Wide tile only)
#
# All four assets share a single brand bell silhouette. Same path data as the
# tray icon's CreateBellIcon (TrayIconService.cs) so the tray, the Start tile,
# and the Store listing all read as the same product.
#
# Sizes match the MSIX manifest in src/ToastRevival.Agent/Package.appxmanifest:
#   Square44x44Logo.png     44 × 44   taskbar / list small
#   Square150x150Logo.png  150 × 150  medium tile (default Start tile)
#   Wide310x150Logo.png    310 × 150  wide tile (with wordmark)
#   StoreLogo.png           50 × 50   Store listing thumbnail

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

if (-not $OutputDir) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $OutputDir = Join-Path $repoRoot "src\ToastRevival.Agent\Images"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$panelDark   = [System.Drawing.Color]::FromArgb(10, 15, 26)    # #0A0F1A near-black
$brandAmber   = [System.Drawing.Color]::FromArgb(245, 158, 11)   # #F59E0B brand amber
$amberGlow    = [System.Drawing.Color]::FromArgb(40, 245, 158, 11) # 16% alpha for halo
$textPrimary = [System.Drawing.Color]::FromArgb(240, 240, 245) # #F0F0F5 wordmark

<#
Returns a closed System.Drawing.Drawing2D.GraphicsPath for the brand bell
silhouette, scaled into the rectangle (x, y, width, height). The bell is
composed of stem rectangle + dome ellipse + body polygon + clapper disc --
identical proportions to TrayIconService.CreateBellIcon so the tray and
tile assets read as the same icon at every size.
#>
function New-BellPath {
    param(
        [single] $X,
        [single] $Y,
        [single] $W,
        [single] $H
    )
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath

    # Stem (bell crown -- small rectangle at the top).
    $rectX = [single]($X + 0.45 * $W)
    $rectY = [single]($Y + 0.10 * $H)
    $rectW = [single](0.10 * $W)
    $rectH = [single](0.06 * $H)
    $path.AddRectangle((New-Object System.Drawing.RectangleF($rectX, $rectY, $rectW, $rectH)))

    # Bell body -- closed polygon (top of dome down to the flared rim).
    $body = @(
        (New-Object System.Drawing.PointF([single]($X + 0.32 * $W), [single]($Y + 0.16 * $H))),
        (New-Object System.Drawing.PointF([single]($X + 0.32 * $W), [single]($Y + 0.50 * $H))),
        (New-Object System.Drawing.PointF([single]($X + 0.22 * $W), [single]($Y + 0.66 * $H))),
        (New-Object System.Drawing.PointF([single]($X + 0.18 * $W), [single]($Y + 0.74 * $H))),
        (New-Object System.Drawing.PointF([single]($X + 0.82 * $W), [single]($Y + 0.74 * $H))),
        (New-Object System.Drawing.PointF([single]($X + 0.78 * $W), [single]($Y + 0.66 * $H))),
        (New-Object System.Drawing.PointF([single]($X + 0.68 * $W), [single]($Y + 0.50 * $H))),
        (New-Object System.Drawing.PointF([single]($X + 0.68 * $W), [single]($Y + 0.16 * $H)))
    )
    $path.AddPolygon([System.Drawing.PointF[]]$body)

    # Smooth top of dome with an ellipse so the bell crown doesn't look boxy.
    $path.AddEllipse(
        [single]($X + 0.28 * $W), [single]($Y + 0.10 * $H),
        [single](0.44 * $W),       [single](0.20 * $H))

    # Clapper -- small disc just below the rim.
    $path.AddEllipse(
        [single]($X + 0.43 * $W), [single]($Y + 0.78 * $H),
        [single](0.14 * $W),       [single](0.14 * $H))

    return $path
}

function New-PanelTile {
    param(
        [int]    $Width,
        [int]    $Height,
        [string] $Path,
        [single] $BellPaddingPct = 0.18
    )

    $bitmap   = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    # Solid near-black background.
    $bg = New-Object System.Drawing.SolidBrush($panelDark)
    $graphics.FillRectangle($bg, 0, 0, $Width, $Height)
    $bg.Dispose()

    # Bell -- pad inward so the silhouette has breathing room around the rim.
    $shortest = [Math]::Min($Width, $Height)
    $padX     = [single]($shortest * $BellPaddingPct)
    $bellW    = [single]($Width  - 2 * $padX)
    $bellH    = [single]($Height - 2 * $padX)
    $bellX    = [single](($Width  - $bellW) / 2)
    $bellY    = [single](($Height - $bellH) / 2)

    # Subtle teal halo behind the bell -- softens the high-contrast bell-on-panel
    # at large sizes (150+). Skipped at 44/50 because the halo just adds noise.
    if ($shortest -ge 100) {
        $haloBrush = New-Object System.Drawing.SolidBrush($amberGlow)
        $haloPad   = [single]($shortest * 0.06)
        $graphics.FillEllipse($haloBrush,
            ($bellX - $haloPad), ($bellY - $haloPad),
            ($bellW + 2 * $haloPad), ($bellH + 2 * $haloPad))
        $haloBrush.Dispose()
    }

    $bell = New-BellPath -X $bellX -Y $bellY -W $bellW -H $bellH
    $brush = New-Object System.Drawing.SolidBrush($brandAmber)
    $graphics.FillPath($brush, $bell)
    $brush.Dispose()
    $bell.Dispose()

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}

function New-WidePanelTile {
    param(
        [int]    $Width,
        [int]    $Height,
        [string] $Path,
        [string] $Wordmark
    )

    $bitmap   = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    # Solid near-black background.
    $bg = New-Object System.Drawing.SolidBrush($panelDark)
    $graphics.FillRectangle($bg, 0, 0, $Width, $Height)
    $bg.Dispose()

    # Bell on the left third of the wide tile.
    $bellPad  = [single]($Height * 0.18)
    $bellSize = [single]($Height - 2 * $bellPad)
    $bellX    = [single]($bellPad + 4)
    $bellY    = [single](($Height - $bellSize) / 2)

    $haloBrush = New-Object System.Drawing.SolidBrush($amberGlow)
    $haloPad   = [single]($Height * 0.05)
    $graphics.FillEllipse($haloBrush,
        ($bellX - $haloPad), ($bellY - $haloPad),
        ($bellSize + 2 * $haloPad), ($bellSize + 2 * $haloPad))
    $haloBrush.Dispose()

    $bell = New-BellPath -X $bellX -Y $bellY -W $bellSize -H $bellSize
    $brush = New-Object System.Drawing.SolidBrush($brandAmber)
    $graphics.FillPath($brush, $bell)
    $brush.Dispose()
    $bell.Dispose()

    # Wordmark to the right of the bell. Two-line stack: "Toast" / "Notification"
    # at 22pt + 16pt -- keeps the brand readable inside the 310×150 frame.
    $wordmarkX = [single]($bellX + $bellSize + 14)
    $line1     = "Toast"
    $line2     = "Notification"
    $font1     = New-Object System.Drawing.Font("Segoe UI", 22, [System.Drawing.FontStyle]::Bold)
    $font2     = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Regular)
    $fg        = New-Object System.Drawing.SolidBrush($textPrimary)

    $size1 = $graphics.MeasureString($line1, $font1)
    $size2 = $graphics.MeasureString($line2, $font2)
    $totalH = $size1.Height + $size2.Height - 4
    $textY  = [single](($Height - $totalH) / 2)

    $graphics.DrawString($line1, $font1, $fg, $wordmarkX, $textY)
    $graphics.DrawString($line2, $font2, $fg, $wordmarkX, $textY + $size1.Height - 4)

    $fg.Dispose(); $font1.Dispose(); $font2.Dispose()

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}

$square44  = Join-Path $OutputDir "Square44x44Logo.png"
$square150 = Join-Path $OutputDir "Square150x150Logo.png"
$wide310   = Join-Path $OutputDir "Wide310x150Logo.png"
$store     = Join-Path $OutputDir "StoreLogo.png"

New-PanelTile     -Width 44  -Height 44  -Path $square44  -BellPaddingPct 0.10
New-PanelTile     -Width 150 -Height 150 -Path $square150 -BellPaddingPct 0.18
New-PanelTile     -Width 50  -Height 50  -Path $store     -BellPaddingPct 0.10
New-WidePanelTile -Width 310 -Height 150 -Path $wide310   -Wordmark "Toast Notification"

Write-Host "Generated MSIX tile assets (M9.C -- production brand):"
Write-Host "  $square44"
Write-Host "  $square150"
Write-Host "  $wide310"
Write-Host "  $store"
