[CmdletBinding()]
param(
    [string] $Version = "0.4.0.0",
    [string] $RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot       = Split-Path -Parent $PSScriptRoot
$projectPath    = Join-Path $repoRoot "src\ToastRevival.Agent\ToastRevival.Agent.csproj"
$agentSrcDir    = Join-Path $repoRoot "src\ToastRevival.Agent"
$publishDir     = Join-Path $repoRoot "artifacts\ToastRevival.Agent\$RuntimeIdentifier-self-contained"
$installerSrc    = Join-Path $repoRoot "installer\ToastRevival.Agent.Setup.wxs"
$logonTaskXml    = Join-Path $repoRoot "installer\ToastNotificationLogon.xml"
$updaterTaskXml  = Join-Path $repoRoot "installer\ToastNotificationUpdater.xml"
$installerOut   = Join-Path $repoRoot "artifacts\installer"
$msiPath        = Join-Path $installerOut "ToastNotification.Agent-$Version.msi"

if (-not (Test-Path $logonTaskXml))    { throw "Logon task XML not found: $logonTaskXml" }
if (-not (Test-Path $updaterTaskXml)) { throw "Updater task XML not found: $updaterTaskXml" }

Write-Host "==> Publishing self-contained agent ($RuntimeIdentifier)..."
dotnet publish $projectPath `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -p:SatelliteResourceLanguages=en `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

if (-not (Test-Path $publishDir)) { throw "Publish directory not found: $publishDir" }

# Strip all non-English locale resource folders. Both .NET and Windows App SDK
# publish satellite assemblies for every supported locale. We only ship English.
$stripped = 0
Get-ChildItem $publishDir -Directory |
    Where-Object { $_.Name -match '^\w{2,3}(-\w+)*$' -and $_.Name -notlike 'en*' } |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force; $stripped++ }
if ($stripped -gt 0) { Write-Host "==> Stripped $stripped non-English locale folders." }

New-Item -ItemType Directory -Force -Path $installerOut | Out-Null

$wix = Get-Command wix.exe -ErrorAction SilentlyContinue
if (-not $wix) {
    $wix = Get-Item "$env:USERPROFILE\.dotnet\tools\wix.exe" -ErrorAction Stop
}

$licenseRtf = Join-Path $repoRoot "installer\License.rtf"
if (-not (Test-Path $licenseRtf)) { throw "License RTF not found: $licenseRtf" }

Write-Host "==> Building MSI ($Version) -> $msiPath"
& $wix.Source build $installerSrc `
    -arch x64 `
    -ext WixToolset.UI.wixext `
    -d "PublishDir=$publishDir" `
    -d "ProductVersion=$Version" `
    -d "LogonTaskXmlPath=$logonTaskXml" `
    -d "UpdaterTaskXmlPath=$updaterTaskXml" `
    -d "LicenseRtf=$licenseRtf" `
    -d "AgentSrcDir=$agentSrcDir" `
    -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "wix build failed (exit $LASTEXITCODE)" }

$msi = Get-Item $msiPath
Write-Host ""
Write-Host "MSI ready:"
Write-Host "  Path : $($msi.FullName)"
Write-Host ("  Size : {0:N2} MB" -f ($msi.Length / 1MB))
