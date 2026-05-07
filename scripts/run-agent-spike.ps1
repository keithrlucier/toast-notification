[CmdletBinding()]
param(
    [ValidateSet("plain", "announcement", "alert", "action", "reminder", "celebration", "maintenance")]
    [string] $Template = "plain",
    [int]    $WaitSeconds = 15,
    [string] $Title,
    [string] $Body
)

$ErrorActionPreference = "Stop"

$repoRoot    = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\ToastRevival.Agent\ToastRevival.Agent.csproj"

$dotnetArgs = @('run', '--project', $projectPath, '--', '--template', $Template, '--wait', "$WaitSeconds")
if ($PSBoundParameters.ContainsKey('Title')) { $dotnetArgs += @('--title', $Title) }
if ($PSBoundParameters.ContainsKey('Body'))  { $dotnetArgs += @('--body',  $Body)  }

dotnet @dotnetArgs
