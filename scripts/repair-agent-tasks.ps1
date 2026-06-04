<#
.SYNOPSIS
    Remediation script for the MajorUpgrade task-deletion bug (0.4.39-fix).

.DESCRIPTION
    Affected versions: any endpoint upgraded via MSI to 0.4.38 or earlier where
    MajorUpgrade afterInstallFinalize caused the old product's REMOVE="ALL" uninstall
    to delete \Toast2IT\ToastNotificationAgentLogon and \Toast2IT\ToastNotificationUpdater
    after the new MSI had just created them.

    Symptom: ToastNotification.Agent.exe is present and current in C:\Program Files\Toast Notification,
    but the device shows offline in the tenant dashboard and never self-updates because no
    scheduled task fires at user logon.

    This script:
      1. Verifies the agent is installed.
      2. Re-creates the logon task and the SYSTEM updater task from their XML files.
      3. Fires the logon task so the current logged-on user's agent starts immediately
         (without requiring a logoff/logon cycle).

.NOTES
    Run as SYSTEM or a local admin account.
    Safe to run on healthy machines — /F flag makes task creation idempotent.
    Deploy via RMM as a one-time policy against all enrolled endpoints.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$InstallDir    = Join-Path $env:ProgramFiles 'Toast Notification'
$AgentExe      = Join-Path $InstallDir 'ToastNotification.Agent.exe'
$LogonTaskXml  = Join-Path $InstallDir 'ToastNotificationLogon.xml'
$UpdaterTaskXml = Join-Path $InstallDir 'ToastNotificationUpdater.xml'
$LogonTaskName  = '\Toast2IT\ToastNotificationAgentLogon'
$UpdaterTaskName = '\Toast2IT\ToastNotificationUpdater'

function Write-Status([string]$msg) { Write-Output "[$(Get-Date -Format 'HH:mm:ss')] $msg" }

# ── 1. Verify install ────────────────────────────────────────────────────────
if (-not (Test-Path $AgentExe)) {
    Write-Status "SKIP: Toast Notification Agent not installed at $AgentExe"
    exit 0
}

if (-not (Test-Path $LogonTaskXml)) {
    Write-Status "ERROR: Logon task XML missing at $LogonTaskXml — reinstall required"
    exit 1
}

if (-not (Test-Path $UpdaterTaskXml)) {
    Write-Status "ERROR: Updater task XML missing at $UpdaterTaskXml — reinstall required"
    exit 1
}

# ── 2. Check current task state ──────────────────────────────────────────────
$logonExists   = $null -ne (Get-ScheduledTask -TaskPath '\Toast2IT\' -TaskName 'ToastNotificationAgentLogon' -ErrorAction SilentlyContinue)
$updaterExists = $null -ne (Get-ScheduledTask -TaskPath '\Toast2IT\' -TaskName 'ToastNotificationUpdater'   -ErrorAction SilentlyContinue)

if ($logonExists -and $updaterExists) {
    Write-Status "Tasks already present — verifying agent process."
}
else {
    Write-Status "Missing tasks detected — logon=$logonExists updater=$updaterExists. Recreating."
}

# ── 3. (Re)create the logon task ─────────────────────────────────────────────
Write-Status "Creating $LogonTaskName ..."
$result = & "$env:SystemRoot\System32\schtasks.exe" /Create /TN $LogonTaskName /XML $LogonTaskXml /F 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Status "ERROR: schtasks /Create logon task failed (exit $LASTEXITCODE): $result"
    exit 2
}
Write-Status "Logon task created."

# ── 4. (Re)create the SYSTEM updater task ───────────────────────────────────
Write-Status "Creating $UpdaterTaskName ..."
$result = & "$env:SystemRoot\System32\schtasks.exe" /Create /TN $UpdaterTaskName /XML $UpdaterTaskXml /F 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Status "ERROR: schtasks /Create updater task failed (exit $LASTEXITCODE): $result"
    exit 3
}
Write-Status "Updater task created."

# ── 5. Fire the logon task to bring the agent up immediately ─────────────────
# schtasks /Run launches the task in the logged-on user's session (same as the
# LogonTrigger would). Returns 1 if no interactive session — that is acceptable;
# the task will fire at next logon.
Write-Status "Starting agent now via $LogonTaskName ..."
$result = & "$env:SystemRoot\System32\schtasks.exe" /Run /TN $LogonTaskName 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Status "NOTE: schtasks /Run returned exit $LASTEXITCODE (no interactive session?) — agent will start at next logon. $result"
}
else {
    Write-Status "Agent task fired."
}

# ── 6. Brief settle then check for running process ───────────────────────────
Start-Sleep -Seconds 4
$proc = Get-Process -Name 'ToastNotification.Agent' -ErrorAction SilentlyContinue
if ($proc) {
    Write-Status "SUCCESS: ToastNotification.Agent is running (PID $($proc.Id))."
    exit 0
}
else {
    Write-Status "NOTE: Agent process not detected after 4 s. It may start shortly, or no interactive session is active."
    exit 0
}
