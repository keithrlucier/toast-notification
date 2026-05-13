# Signs an MSIX (or MSI / EXE / DLL) with the Sectigo OV cert on the
# Thales SafeNet hardware token.
#
# REQUIREMENTS
#   1. SafeNet Authentication Client running, token plugged in, token logged
#      in via the SafeNet tray app (same setup the DigiCert Utility uses).
#   2. signtool.exe somewhere on disk. Two reliable locations:
#        a. Windows SDK:   C:\Program Files (x86)\Windows Kits\10\bin\<ver>\x64\signtool.exe
#                          (only if "Windows SDK Signing Tools for Desktop Apps"
#                          was selected during SDK install)
#        b. NuGet cache:   %USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\<ver>\bin\<ver>\x64\signtool.exe
#                          (ALWAYS present after a successful MSIX build because
#                          WinAppSDK brings it transitively)
#   This script searches both.
#
# PROJECT CONTEXT
#   The DigiCert Certificate Utility (v2.x) does NOT sign MSIX -- it handles
#   .exe/.dll/.msi only. signtool.exe is the canonical MSIX signing tool.
#
#   Package.Identity.Publisher in Package.appxmanifest must contain EVERY
#   RDN from the cert Subject in the cert's order with the cert's quoting,
#   or the sign rejects with 0x80091005 / 0x800B0109. The Sectigo OV cert
#   subject for this project is:
#     CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US
#   (CN, O, S, C -- four RDNs. CN and O contain commas so both need quotes.)

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Path,

    [string] $TimestampUrl = "http://timestamp.digicert.com",
    [string] $DigestAlgorithm = "SHA256"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Path)) {
    throw "File to sign not found: $Path"
}

$searchPaths = @(
    "C:\Program Files (x86)\Windows Kits",
    "C:\Program Files\Windows Kits",
    (Join-Path $env:USERPROFILE ".nuget\packages")
) | Where-Object { Test-Path $_ }

Write-Host "==> Locating signtool.exe (x64) under:"
$searchPaths | ForEach-Object { Write-Host "      $_" }

$signtool = (Get-ChildItem -Path $searchPaths -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\x64\*" } |
    Sort-Object @{Expression = { $_.VersionInfo.FileVersion }; Descending = $true } |
    Select-Object -First 1).FullName

if (-not $signtool) {
    throw "signtool.exe not found. Either install the Windows SDK Signing Tools for Desktop Apps, or run a successful WinAppSDK build first to populate the NuGet cache copy."
}

Write-Host "==> signtool: $signtool"
Write-Host "==> Signing : $Path"
Write-Host "==> Timestamp: $TimestampUrl"
Write-Host ""
Write-Host "    SafeNet PIN dialog will pop. Enter the token PIN when prompted."
Write-Host ""

& $signtool sign `
    /a `
    /fd $DigestAlgorithm `
    /tr $TimestampUrl `
    /td $DigestAlgorithm `
    "$Path"

if ($LASTEXITCODE -ne 0) {
    throw "signtool returned exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "==> Verifying signature"
$sig = Get-AuthenticodeSignature -LiteralPath $Path
$sig | Format-List Status, StatusMessage,
    @{ n = "Signer"; e = { $_.SignerCertificate.Subject } },
    @{ n = "Issuer"; e = { $_.SignerCertificate.Issuer } },
    @{ n = "NotAfter"; e = { $_.SignerCertificate.NotAfter } },
    @{ n = "Thumbprint"; e = { $_.SignerCertificate.Thumbprint } },
    @{ n = "Timestamp"; e = { $_.TimeStamperCertificate.Subject } }

if ($sig.Status -ne "Valid") {
    throw "Signature verification did not return Valid (got: $($sig.Status))."
}

Write-Host "Signed and verified." -ForegroundColor Green
