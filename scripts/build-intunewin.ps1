[CmdletBinding()]
param(
    # Four-part MSI version, e.g. "0.4.41.0". Defaults to the newest signed MSI
    # found in artifacts\installer.
    [string] $Version
)

$ErrorActionPreference = "Stop"

$repoRoot    = Split-Path -Parent $PSScriptRoot
$installerOut = Join-Path $repoRoot "artifacts\installer"
$toolPath    = Join-Path $repoRoot "artifacts\intune-tool\IntuneWinAppUtil.exe"
$intuneOut   = Join-Path $repoRoot "artifacts\intune-out"
# Dedicated, single-file staging dir. IntuneWinAppUtil -c compresses the WHOLE
# source folder, so the stage must hold ONLY the canonical MSI — any stray file
# (e.g. a previous version's MSI) would be bundled into the package and bloat it.
$stageDir    = Join-Path $repoRoot "artifacts\intune-stage"

if (-not (Test-Path $toolPath)) {
    throw "IntuneWinAppUtil.exe not found: $toolPath`nDownload it from https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool/releases and place it there."
}

# Resolve the source MSI. If no version was passed, pick the newest by version.
if ($Version) {
    $srcMsi = Join-Path $installerOut "ToastNotification.Agent-$Version.msi"
    if (-not (Test-Path $srcMsi)) { throw "Source MSI not found: $srcMsi" }
} else {
    $candidate = Get-ChildItem $installerOut -Filter "ToastNotification.Agent-*.msi" -ErrorAction SilentlyContinue |
        Sort-Object { [version](($_.BaseName -replace '^ToastNotification\.Agent-', '')) } -Descending |
        Select-Object -First 1
    if (-not $candidate) { throw "No ToastNotification.Agent-*.msi found in $installerOut" }
    $srcMsi  = $candidate.FullName
    $Version = $candidate.BaseName -replace '^ToastNotification\.Agent-', ''
}

# Confirm the source MSI is Authenticode-signed before wrapping — an unsigned MSI
# inside an Intune package fails SmartScreen / WDAC on managed endpoints.
$sig = Get-AuthenticodeSignature $srcMsi
if ($sig.Status -ne 'Valid') {
    throw "Source MSI is not validly signed (status: $($sig.Status)): $srcMsi`nSign the MSI before wrapping it for Intune."
}
Write-Host "==> Source MSI signed OK: $($sig.SignerCertificate.Subject)"

# IntuneWinAppUtil names the output after the setup file and Intune's default
# install command references that name. Stage the versioned MSI under the canonical
# name so the produced package + the install command both say "ToastNotification.msi".
# Wipe-and-recreate the stage so it contains exactly one file (see note above).
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stageDir  | Out-Null
New-Item -ItemType Directory -Force -Path $intuneOut | Out-Null
$canonMsi = Join-Path $stageDir "ToastNotification.msi"
Copy-Item $srcMsi $canonMsi -Force

Write-Host "==> Wrapping $([System.IO.Path]::GetFileName($srcMsi)) ($Version) -> .intunewin"
& $toolPath -c $stageDir -s "ToastNotification.msi" -o $intuneOut -q
if ($LASTEXITCODE -ne 0) { throw "IntuneWinAppUtil failed (exit $LASTEXITCODE)" }

# Tool emits <setup-basename>.intunewin (ToastNotification.intunewin). Keep a
# version-stamped copy for the archive and the canonical name for deploy/serving.
$produced = Join-Path $intuneOut "ToastNotification.intunewin"
if (-not (Test-Path $produced)) { throw "Expected output not found: $produced" }
$versioned = Join-Path $intuneOut "ToastNotification.Agent-$Version.intunewin"
Copy-Item $produced $versioned -Force

$out = Get-Item $produced
Write-Host ""
Write-Host ".intunewin ready:"
Write-Host "  Canonical : $($out.FullName)"
Write-Host "  Versioned : $versioned"
Write-Host ("  Size      : {0:N2} MB" -f ($out.Length / 1MB))
Write-Host ""
Write-Host "Deploy: copy ToastNotification.intunewin to the server downloads dir"
Write-Host "        (Downloads:RootPath, e.g. /opt/toast/downloads/) alongside the MSI."
