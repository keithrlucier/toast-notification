[CmdletBinding()]
param(
    [ValidateSet("framework-dependent", "self-contained", "both")]
    [string] $Mode = "both",
    [string] $RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\ToastRevival.Agent\ToastRevival.Agent.csproj"
$artifactRoot = Join-Path $repoRoot "artifacts\ToastRevival.Agent"

if ($Mode -eq "framework-dependent" -or $Mode -eq "both") {
    dotnet publish $projectPath `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained false `
        --output (Join-Path $artifactRoot "$RuntimeIdentifier-framework-dependent")
}

if ($Mode -eq "self-contained" -or $Mode -eq "both") {
    dotnet publish $projectPath `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        -p:WindowsAppSDKSelfContained=true `
        --output (Join-Path $artifactRoot "$RuntimeIdentifier-self-contained")
}
