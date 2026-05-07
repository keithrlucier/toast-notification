[CmdletBinding()]
param(
    [int] $WaitSeconds = 15,
    [string] $Title = "ToastRevival agent spike",
    [string] $Body = "M0A local Windows App SDK notification is working."
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\ToastRevival.Agent\ToastRevival.Agent.csproj"

dotnet run --project $projectPath -- --title $Title --body $Body --wait $WaitSeconds
