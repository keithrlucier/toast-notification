#Requires -RunAsAdministrator
<#
.SYNOPSIS
    M0 D4 verification script — GPO / multi-user / uninstall / upgrade matrix.

.DESCRIPTION
    Run at each checkpoint listed below. Outputs pass/fail for each check.
    Requires admin elevation. Intended for a Windows 11 lab/staging machine.

.PARAMETER Phase
    Check        — snapshot current task + notification policy state (safe, read-only)
    PostInstall  — verify task after fresh install or upgrade
    PostUninstall — verify task is gone after MSI uninstall
    MultiUser    — confirm task registered for BUILTIN\Users (fires for any user)
    GPOBlock     — simulate "Turn off App Notifications" policy and verify agent behavior
    GPOUnblock   — remove the simulated policy key

.EXAMPLE
    .\scripts\verify-d4-matrix.ps1 -Phase Check
    .\scripts\verify-d4-matrix.ps1 -Phase PostInstall
    .\scripts\verify-d4-matrix.ps1 -Phase PostUninstall
    .\scripts\verify-d4-matrix.ps1 -Phase GPOBlock
    .\scripts\verify-d4-matrix.ps1 -Phase GPOUnblock
#>
[CmdletBinding()]
param(
    [ValidateSet('Check','PostInstall','PostUninstall','MultiUser','GPOBlock','GPOUnblock')]
    [string] $Phase = 'Check'
)

$ErrorActionPreference = 'Stop'
$TaskPath = '\Toast2IT\'
$TaskName = 'ToastNotificationAgentLogon'
$FullTaskName = "$TaskPath$TaskName"
$ExpectedExe = "$env:ProgramFiles\Toast Notification\ToastNotification.Agent.exe"
$GPOPushKey = 'HKCU:\Software\Policies\Microsoft\Windows\CurrentVersion\PushNotifications'
$GPOValueName = 'NoToastApplicationNotification'

function Pass([string]$msg) { Write-Host "  PASS  $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "  FAIL  $msg" -ForegroundColor Red }
function Info([string]$msg) { Write-Host "  INFO  $msg" -ForegroundColor Cyan }
function Head([string]$msg) { Write-Host "`n=== $msg ===" -ForegroundColor Yellow }

# ─── PHASE: Check ────────────────────────────────────────────────────────────
if ($Phase -eq 'Check') {
    Head 'Task state'
    $task = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($task) {
        Pass "Task exists at $FullTaskName (State=$($task.State))"
        $trigger = $task.Triggers | Where-Object { $_.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger' }
        if ($trigger) { Pass "LogonTrigger present (Enabled=$($trigger.Enabled))" }
        else           { Fail "LogonTrigger NOT found" }
        $principal = $task.Principal
        if ($principal.GroupId -eq 'S-1-5-32-545') { Pass "Principal=BUILTIN\Users (S-1-5-32-545)" }
        else { Fail "Principal GroupId=$($principal.GroupId) — expected S-1-5-32-545" }
        if ($principal.RunLevel -eq 'LeastPrivilege') { Pass "RunLevel=LeastPrivilege" }
        else { Fail "RunLevel=$($principal.RunLevel) — expected LeastPrivilege" }
        $action = $task.Actions | Select-Object -First 1
        if ($action.Execute -and $action.Execute.TrimEnd('"').TrimStart('"') -like "*ToastNotification.Agent.exe") {
            Pass "Action Execute points at ToastNotification.Agent.exe"
        } else { Fail "Action Execute='$($action.Execute)' — unexpected path" }
        if ($action.Arguments -eq '--template alert --no-wait') { Pass "Action Arguments='--template alert --no-wait'" }
        else { Info "Action Arguments='$($action.Arguments)'" }
    } else {
        Info "Task $FullTaskName not present (expected if MSI is not installed)"
    }

    Head 'Notification policy (current user)'
    $noToast = Get-ItemProperty -Path $GPOPushKey -Name $GPOValueName -ErrorAction SilentlyContinue
    if ($noToast -and $noToast.$GPOValueName -eq 1) {
        Info "NoToastApplicationNotification=1 — toasts are GPO-blocked for this user"
    } else {
        Pass "NoToastApplicationNotification not set — toasts allowed"
    }

    Head 'Agent binary'
    if (Test-Path $ExpectedExe) { Pass "Agent binary found at $ExpectedExe" }
    else { Info "Agent binary not at $ExpectedExe (expected if MSI not installed)" }

    Head 'Installed MSI versions (Add/Remove Programs)'
    $msiEntries = Get-ItemProperty `
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like '*Toast Notification*' }
    if ($msiEntries) {
        foreach ($e in $msiEntries) {
            Info "Installed: $($e.DisplayName) v$($e.DisplayVersion)"
        }
    } else {
        Info "No Toast Notification MSI entry found in Add/Remove Programs"
    }

    Head 'User sessions'
    $sessions = query session 2>$null
    Info "Active sessions:`n$(($sessions | Select-Object -Skip 1) -join "`n")"
}

# ─── PHASE: PostInstall ───────────────────────────────────────────────────────
if ($Phase -eq 'PostInstall') {
    Head 'Post-install verification'
    $task = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) { Fail "Task $FullTaskName NOT found after install"; exit 1 }
    Pass "Task exists (State=$($task.State))"
    if ($task.State -eq 'Ready') { Pass "State=Ready" }
    else { Fail "State=$($task.State) — expected Ready" }
    $principal = $task.Principal
    if ($principal.GroupId -eq 'S-1-5-32-545') { Pass "Principal=BUILTIN\Users" }
    else { Fail "Principal GroupId=$($principal.GroupId)" }
    if ($principal.RunLevel -eq 'LeastPrivilege') { Pass "RunLevel=LeastPrivilege" }
    else { Fail "RunLevel=$($principal.RunLevel)" }
    if (Test-Path $ExpectedExe) { Pass "Agent binary present at $ExpectedExe" }
    else { Fail "Agent binary NOT found at $ExpectedExe" }
    Info "Run 'Get-ScheduledTask -TaskPath \Toast2IT\ | Format-List *' for full task detail."
    Info "Log out and back in to verify the toast fires."
}

# ─── PHASE: PostUninstall ─────────────────────────────────────────────────────
if ($Phase -eq 'PostUninstall') {
    Head 'Post-uninstall verification'
    $task = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($task) { Fail "Task $FullTaskName STILL EXISTS after uninstall — WiX UninstallScheduledTask did not fire" }
    else        { Pass "Task $FullTaskName is gone" }
    if (Test-Path $ExpectedExe) { Fail "Agent binary still present at $ExpectedExe" }
    else                        { Pass "Agent binary removed from $ExpectedExe" }
    if (Test-Path "$env:ProgramFiles\Toast Notification") {
        Info "Install folder still present — may contain residual user data or logs"
    } else {
        Pass "Install folder removed"
    }
}

# ─── PHASE: MultiUser ─────────────────────────────────────────────────────────
if ($Phase -eq 'MultiUser') {
    Head 'Multi-user task registration check'
    $task = Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) { Fail "Task not found — install the MSI first"; exit 1 }
    $principal = $task.Principal
    if ($principal.GroupId -eq 'S-1-5-32-545') {
        Pass "Principal=BUILTIN\Users (S-1-5-32-545) — task fires for ALL logged-in users"
    } else {
        Fail "Principal is $($principal.GroupId) — task fires for a specific user only, not group-wide"
    }
    Info "MANUAL STEP: Create a second local user account (if not already present):"
    Info "  net user TestUser2 Password123! /add"
    Info "Log out, log in as TestUser2, verify the toast fires."
    Info "Log back in as the admin user and confirm toast fires again."
    Info "Use 'query session' to see concurrent sessions if fast-user-switching is active."
}

# ─── PHASE: GPOBlock ─────────────────────────────────────────────────────────
if ($Phase -eq 'GPOBlock') {
    Head 'Simulating GPO: Turn off App Notifications'
    Info "Setting HKCU NoToastApplicationNotification=1 for current user..."
    if (-not (Test-Path $GPOPushKey)) {
        New-Item -Path $GPOPushKey -Force | Out-Null
    }
    Set-ItemProperty -Path $GPOPushKey -Name $GPOValueName -Value 1 -Type DWORD -Force
    $verify = (Get-ItemProperty -Path $GPOPushKey -Name $GPOValueName).$GPOValueName
    if ($verify -eq 1) { Pass "Policy key set: $GPOPushKey\$GPOValueName = 1" }
    else               { Fail "Failed to set policy key" }
    Info ""
    Info "EXPECTED BEHAVIOR: Agent scheduled task will still fire at logon."
    Info "The agent process (ToastNotification.Agent.exe) will run and exit 0."
    Info "No toast will appear — Windows suppresses all app notifications for this user."
    Info "This is correct behavior: the agent cannot override a notification GPO."
    Info ""
    Info "VERIFICATION:"
    Info "  1. Log out and back in."
    Info "  2. Confirm: scheduled task fires (Get-ScheduledTask shows LastRunTime updated)."
    Info "  3. Confirm: NO toast appears (silent suppression by Windows)."
    Info "  4. Confirm: agent.log shows Register()/Show() returned without throwing."
    Info "     Log path: `$env:LOCALAPPDATA\Toast2IT\Toast Notification\agent.log"
    Info ""
    Info "Run '-Phase GPOUnblock' to restore toast behavior after testing."
}

# ─── PHASE: GPOUnblock ────────────────────────────────────────────────────────
if ($Phase -eq 'GPOUnblock') {
    Head 'Removing simulated GPO policy'
    $noToast = Get-ItemProperty -Path $GPOPushKey -Name $GPOValueName -ErrorAction SilentlyContinue
    if ($noToast) {
        Remove-ItemProperty -Path $GPOPushKey -Name $GPOValueName -Force
        Pass "NoToastApplicationNotification removed — toasts re-enabled for this user"
    } else {
        Info "NoToastApplicationNotification was not set — nothing to remove"
    }
}
