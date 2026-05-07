[CmdletBinding()]
param(
    [string] $Version = "0.2.0.0",
    [string] $Platform = "x64",
    [switch] $SkipAssetGeneration
)

$ErrorActionPreference = "Stop"

$repoRoot     = Split-Path -Parent $PSScriptRoot
$projectPath  = Join-Path $repoRoot "src\ToastRevival.Agent\ToastRevival.Agent.csproj"
$manifestPath = Join-Path $repoRoot "src\ToastRevival.Agent\Package.appxmanifest"
$imagesDir    = Join-Path $repoRoot "src\ToastRevival.Agent\Images"
$outputDir    = Join-Path $repoRoot "artifacts\installer\msix"

if (-not (Test-Path $manifestPath)) {
    throw "Package.appxmanifest not found: $manifestPath"
}

if (-not $SkipAssetGeneration) {
    Write-Host "==> Generating MSIX tile assets..."
    & (Join-Path $PSScriptRoot "generate-msix-tile-assets.ps1") -OutputDir $imagesDir
    if ($LASTEXITCODE -ne 0) { throw "Tile asset generation failed (exit $LASTEXITCODE)" }
}

$expectedAssets = @(
    "Square44x44Logo.png",
    "Square150x150Logo.png",
    "Wide310x150Logo.png",
    "StoreLogo.png"
)
foreach ($asset in $expectedAssets) {
    $assetPath = Join-Path $imagesDir $asset
    if (-not (Test-Path $assetPath)) {
        throw "Required tile asset missing: $assetPath"
    }
}

Write-Host "==> Stamping manifest version $Version (in-memory)"
$manifestXml = [xml](Get-Content -Raw -LiteralPath $manifestPath)
$ns = New-Object System.Xml.XmlNamespaceManager($manifestXml.NameTable)
$ns.AddNamespace("p", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
$identityNode = $manifestXml.SelectSingleNode("/p:Package/p:Identity", $ns)
if (-not $identityNode) { throw "Could not locate <Identity> in $manifestPath" }
$manifestVersion = $identityNode.GetAttribute("Version")
if ($manifestVersion -ne $Version) {
    Write-Host "    Manifest version is $manifestVersion; build will use -p:AppxPackageVersion=$Version override."
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Write-Host "==> Building MSIX (-c Release -p:Platform=$Platform -p:WindowsPackageType=MSIX)"
& dotnet build $projectPath `
    -c Release `
    -p:Platform=$Platform `
    -p:WindowsPackageType=MSIX `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxPackageVersion=$Version `
    -p:AppxPackageDir="$outputDir\" `
    -p:AppxBundle=Never `
    -p:AppxPackageOutput="$outputDir\ToastNotification.Agent-$Version.msix" `
    -p:UapAppxPackageBuildMode=SideloadOnly
if ($LASTEXITCODE -ne 0) { throw "dotnet build (MSIX) failed (exit $LASTEXITCODE)" }

$msixCandidates = Get-ChildItem -Path $outputDir -Filter "*.msix" -Recurse -ErrorAction SilentlyContinue
if (-not $msixCandidates) {
    throw "No .msix file produced under $outputDir"
}

$msix = $msixCandidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ""
Write-Host "MSIX ready (UNSIGNED - hand off to Keith for Sectigo OV signing):"
Write-Host "  Path : $($msix.FullName)"
Write-Host ("  Size : {0:N2} MB" -f ($msix.Length / 1MB))
Write-Host ""
Write-Host "Verify identity surface before signing:"
Write-Host "  Package.Identity.Publisher must equal cert subject EXACTLY:"
Write-Host "    CN=`"Toast2IT, LLC`", S=Florida, C=US"
Write-Host "  Mismatch causes 0x800B0109 install failure."
