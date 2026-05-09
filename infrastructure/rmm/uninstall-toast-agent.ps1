<#
.SYNOPSIS
    Silent uninstall of the Toast Notification Windows agent. Designed to
    be invoked by RMM tools when decommissioning an endpoint or migrating
    away from the platform.

.DESCRIPTION
    1. Locates the installed product via the Windows Installer registry.
    2. Runs msiexec /x with /qn /norestart so the uninstall is non-interactive.
    3. Returns msiexec's exit code so the RMM can detect failures.

    Idempotent: if the agent is not installed, exits 0 with a log line.

    Does NOT remove the per-user config at %LocalAppData%\Toast2IT\... —
    if a user signs in after uninstall and the config persists in their
    profile, no agent is running so the config is inert. To purge the
    user-scope config, delete %LocalAppData%\Toast2IT\Toast Notification
    in each user profile. RMMs typically have a separate "user profile
    cleanup" workflow for this; not in scope for the uninstaller.

.PARAMETER WorkDir
    Optional. Local directory for the uninstall log. Defaults to
    %ProgramData%\Toast2IT\Install (same as the installer log location).

.PARAMETER TimeoutSeconds
    Optional. msiexec wall-clock timeout. Default 180 (3 minutes).

.EXAMPLE
    .\uninstall-toast-agent.ps1

.NOTES
    Exit codes:
       0  uninstall completed (or agent was not installed)
       4+ msiexec exit code (passed through)
     124  msiexec hung past TimeoutSeconds and was killed

    Run as SYSTEM (RMM agent context) or any user with local admin.
#>
[CmdletBinding()]
param(
    [string] $WorkDir = (Join-Path $env:ProgramData 'Toast2IT\Install'),

    [int] $TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string] $Message, [string] $Level = 'INFO')
    $ts = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')
    $line = "[$ts] [$Level] $Message"
    Write-Host $line
    if ($script:LogFile) {
        try { Add-Content -Path $script:LogFile -Value $line -Encoding utf8 } catch { }
    }
}

if (-not (Test-Path -LiteralPath $WorkDir)) {
    [void] (New-Item -ItemType Directory -Path $WorkDir -Force)
}
$script:LogFile = Join-Path $WorkDir 'uninstall-toast-agent.log'
Write-Log "Toast Notification agent uninstaller started. WorkDir=$WorkDir"

# ── Find the installed product code ───────────────────────────────────────

$productCode = $null
try {
    $uninstallKeys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    $found = Get-ItemProperty -Path $uninstallKeys -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq 'Toast Notification' } |
        Select-Object -First 1
    if ($found) {
        # PSChildName on the registry key is the {ProductCode} GUID
        $productCode = $found.PSChildName
        Write-Log "Found installed product. DisplayVersion=$($found.DisplayVersion) ProductCode=$productCode"
    }
} catch {
    Write-Log "Could not query uninstall registry: $($_.Exception.Message)" 'WARN'
}

if (-not $productCode) {
    Write-Log "Toast Notification is not installed. Nothing to uninstall."
    exit 0
}

# ── msiexec /x ────────────────────────────────────────────────────────────

$msiLog = Join-Path $WorkDir 'msiexec-uninstall.log'
$arguments = @(
    '/x'; $productCode
    '/qn'; '/norestart'
    '/l*v'; "`"$msiLog`""
)

Write-Log "Running: msiexec.exe $($arguments -join ' ')"

$proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments `
    -NoNewWindow -PassThru
if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
    Write-Log "msiexec did not exit within $TimeoutSeconds seconds — killing." 'ERROR'
    try { $proc | Stop-Process -Force } catch { }
    exit 124
}
$exitCode = $proc.ExitCode

switch ($exitCode) {
    0     { Write-Log "msiexec succeeded (exit 0). Uninstall complete."; exit 0 }
    3010  { Write-Log "msiexec succeeded but flagged a reboot pending (exit 3010). Treating as success."; exit 0 }
    1605  { Write-Log "msiexec reports the product is not installed (exit 1605). Treating as success."; exit 0 }
    1602  { Write-Log "msiexec was canceled (exit 1602)." 'ERROR'; exit $exitCode }
    1603  { Write-Log "msiexec fatal error (exit 1603). See $msiLog for verbose log." 'ERROR'; exit $exitCode }
    default { Write-Log "msiexec exit code $exitCode. See $msiLog for verbose log." 'ERROR'; exit $exitCode }
}
