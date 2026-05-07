[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

dotnet restore (Join-Path $repoRoot "ToastRevival.sln")
dotnet build (Join-Path $repoRoot "ToastRevival.sln") --configuration $Configuration --no-restore
