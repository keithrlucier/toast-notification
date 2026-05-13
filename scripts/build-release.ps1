# Build a Velopack release package for the MSI/RMM auto-update channel.
#
# Prerequisites (run once):
#   dotnet tool install -g vpk
#
# Usage:
#   .\scripts\build-release.ps1 -Version 1.0.0 [-OutputDir artifacts\releases]
#
# Outputs:
#   artifacts\releases\ToastNotification.Agent-<version>-win.zip  (full package)
#   artifacts\releases\ToastNotification.Agent-<version>-delta.zip (delta, if prior version present)
#   artifacts\releases\releases.win.json  (Velopack release index consumed by UpdateManager)
#
# Upload the contents of OutputDir to the release feed URL configured in the agent
# (default: https://releases.toastnotification.com/agent/win-x64).
# The feed is a plain static file host — any HTTP server works.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputDir   = "artifacts\releases",
    [string]$PublishDir  = "artifacts\publish\win-x64",
    [string]$PackId      = "ToastNotification.Agent",
    [string]$Runtime     = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot | Split-Path -Parent

Push-Location $root
try {
    Write-Host "==> Building agent (self-contained, $Runtime, $Configuration)" -ForegroundColor Cyan

    dotnet publish src/ToastRevival.Agent/ToastRevival.Agent.csproj `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $PublishDir `
        /p:PublishSingleFile=false  # Velopack must be able to enumerate assemblies

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    Write-Host "==> Packing Velopack release v$Version" -ForegroundColor Cyan

    # vpk is the Velopack CLI tool — install with: dotnet tool install -g vpk
    vpk pack `
        --packId      $PackId `
        --packVersion $Version `
        --packDir     $PublishDir `
        --mainExe     "$PackId.exe" `
        --outputDir   $OutputDir `
        --runtime     $Runtime

    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE" }

    Write-Host ""
    Write-Host "==> Release packages written to: $OutputDir" -ForegroundColor Green
    Write-Host "    Upload the contents of that directory to the update feed URL."
    Write-Host "    Default: https://releases.toastnotification.com/agent/win-x64"

} finally {
    Pop-Location
}
