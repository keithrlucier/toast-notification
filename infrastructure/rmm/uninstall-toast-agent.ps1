<#
.SYNOPSIS
    One-shot clean removal of the Toast Notification Windows agent. Designed
    to be invoked by RMM tools when decommissioning an endpoint or migrating
    away from the platform — and surfaced verbatim in the dashboard's
    "Remove agent" modal for an admin to run by hand.

.DESCRIPTION
    Reverses everything the install side does, in the correct order, so the
    endpoint is left exactly as it was before Toast Notification touched it
    ("do no harm"):

      1. Stops the running agent.
      2. Restores the per-user lock screen to its original image (or the
         Windows default if the snapshot was lost) by running the agent's
         --revert-appearance mode in the interactive user's session via a
         transient scheduled task. This is the ONLY way to touch the per-user
         WinRT lock screen from a SYSTEM/RMM context — the same InteractiveToken
         principal the install-time logon task uses, so no password is needed.
      3. Removes the machine-wide lock screen policy values the install script
         pins (Spotlight / lock screen camera / lock screen overlays). These
         outlive the product otherwise.
      4. Clears the per-user Spotlight toggles (ContentDeliveryManager) from
         every loaded user hive AND the SYSTEM hive — older install scripts ran
         as SYSTEM and wrote HKCU into the wrong profile, so we sweep both.
      5a. Removes every MSI install matched BY NAME in Add/Remove Programs
          (DisplayName -like 'Toast Notification*'). msiexec /x needs a
          ProductCode, so it's read off the matched ARP key — discovered from the
          name, never hardcoded, so it works for any build/version on the fleet.
      5b. Removes the Microsoft Store / MSIX install BY NAME
          (Get-AppxPackage Name -like '*ToastNotification*'), for all users, plus
          the provisioned copy so it doesn't reinstall for new profiles.
      6. Purges the per-user config (%LocalAppData%\Toast2IT\Toast Notification)
         from every user profile so a future reinstall registers fresh.

    Handles both deployment channels (MSI/RMM and Store/MSIX) in one pass, by
    name — no per-build ProductCode GUIDs anywhere.

    Idempotent and best-effort throughout: a missing value, absent task, or
    already-uninstalled product is treated as success. The only failure that
    propagates a non-zero exit is a genuine msiexec error.

.PARAMETER WorkDir
    Optional. Local directory for the uninstall log. Defaults to
    %ProgramData%\Toast2IT\Install (same as the installer log location).

.PARAMETER TimeoutSeconds
    Optional. msiexec wall-clock timeout. Default 180 (3 minutes).

.PARAMETER KeepUserConfig
    Optional switch. Skip step 6 (per-user config purge) — leave the inert
    config.json in place. Rarely needed.

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

    [int] $TimeoutSeconds = 180,

    [switch] $KeepUserConfig
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

# ── Find the installed product code + install path ─────────────────────────

# Name-driven removal — no hardcoded GUIDs. We match our product BY NAME in both
# channels and remove every match:
#   MSI   : Add/Remove Programs DisplayName -like $NameLike. msiexec /x needs the
#           ProductCode, so we read it off the matched ARP key (PSChildName) — the
#           code is DISCOVERED from the name, never hardcoded, so any build works.
#   MSIX  : the Store/sideload package, Name -like $AppxLike (Remove-AppxPackage).
$NameLike = 'Toast Notification*'   # ARP entry is "Toast Notification Agent"
$AppxLike = '*ToastNotification*'   # MSIX Package.Identity.Name is FileUnityCloud.ToastNotification

# Collect every MSI ProductCode whose Add/Remove Programs name matches (handles
# multiple/side-by-side installs and any version — all by name).
$productCodes = @()
try {
    $arpMatches = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like $NameLike -and $_.Publisher -like '*Toast2IT*' }
    foreach ($m in $arpMatches) { $productCodes += $m.PSChildName; Write-Log "MSI match by name: '$($m.DisplayName)' $($m.DisplayVersion) -> $($m.PSChildName)" }
} catch {
    Write-Log "Could not query Add/Remove Programs: $($_.Exception.Message)" 'WARN'
}

# Resolve the agent exe path from the MSI-written InstallPath, falling back to
# the default per-machine location.
$installPath = $null
try {
    $installPath = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Toast2IT\Toast Notification' -Name 'InstallPath' -ErrorAction Stop).InstallPath
} catch { }
if (-not $installPath) { $installPath = Join-Path $env:ProgramFiles 'Toast Notification' }
$agentExe = Join-Path $installPath 'ToastNotification.Agent.exe'

# ── Step 1: stop the running agent ─────────────────────────────────────────

Write-Log "Stopping the Toast Notification agent if running."
try {
    Get-Process -Name 'ToastNotification.Agent' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
} catch { Write-Log "Stop-Process raised: $($_.Exception.Message)" 'WARN' }

# ── Step 2: restore the per-user lock screen (user context, no password) ───

# WinRT LockScreen is a per-user API; SYSTEM cannot call it. We import a
# transient scheduled task that runs the agent's --revert-appearance mode under
# the BUILTIN\Users group with an InteractiveToken (S-1-5-32-545, RunLevel
# LeastPrivilege) — identical to the install-time logon task, so it fires in the
# logged-on user's session with no credentials required. Best-effort: on an
# endpoint with no interactive session, the task simply produces no run and the
# policy revert below (re-enabling Spotlight) is what un-brands the device.
if (Test-Path -LiteralPath $agentExe) {
    Write-Log "Restoring lock screen via agent --revert-appearance (user context)."
    $revertTaskName = '\Toast2IT\ToastNotificationRevertOnce'
    $taskXmlPath = Join-Path $WorkDir 'revert-appearance-task.xml'
    try {
        # Author the task XML. <Arguments> carries --revert-appearance; the
        # group-SID principal + InteractiveToken gives user-context WinRT access.
        $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>Toast2IT, LLC</Author>
    <Description>One-shot: restore the lock screen before Toast Notification removal.</Description>
    <URI>$revertTaskName</URI>
  </RegistrationInfo>
  <Principals>
    <Principal id="Author">
      <GroupId>S-1-5-32-545</GroupId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
    <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$agentExe</Command>
      <Arguments>--revert-appearance</Arguments>
      <WorkingDirectory>$installPath</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@
        # Task Scheduler XML import expects Unicode (UTF-16). PowerShell quotes
        # native-exe arguments with spaces automatically, so pass paths bare.
        [System.IO.File]::WriteAllText($taskXmlPath, $taskXml, [System.Text.Encoding]::Unicode)

        & schtasks.exe /Create /TN $revertTaskName /XML $taskXmlPath /F | Out-Null
        & schtasks.exe /Run /TN $revertTaskName | Out-Null
        Start-Sleep -Seconds 8   # let the short-lived revert run before we yank the exe
        & schtasks.exe /Delete /TN $revertTaskName /F | Out-Null
        Remove-Item -LiteralPath $taskXmlPath -Force -ErrorAction SilentlyContinue
        Write-Log "Lock screen revert task fired and cleaned up."
    } catch {
        Write-Log "Lock screen revert task failed (non-fatal): $($_.Exception.Message)" 'WARN'
    }
} else {
    Write-Log "Agent exe not found at $agentExe — skipping WinRT lock screen restore (policy revert still runs)." 'WARN'
}

# ── Step 3: revert machine-wide lock screen policy values ──────────────────

# Mirror of the install script's HKLM pins. Delete only the named values, never
# the parent keys (which may hold unrelated GPO settings). Re-enabling Spotlight
# is what lets Windows reclaim the lock screen on endpoints where step 2 could
# not run.
Write-Log "Reverting machine lock screen policy values."
$policyValues = @(
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Lock Screen'; Name = 'HideSpotlightWindowsSpotlight' },
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization';            Name = 'NoLockScreenCamera' },
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization';            Name = 'LockScreenOverlaysDisabled' },
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\PushNotifications'; Name = 'NoCloudApplicationNotification' }
)
foreach ($v in $policyValues) {
    try {
        if (Test-Path -LiteralPath $v.Path) {
            Remove-ItemProperty -LiteralPath $v.Path -Name $v.Name -Force -ErrorAction SilentlyContinue
            Write-Log "Removed $($v.Path)\$($v.Name)"
        }
    } catch { Write-Log "Could not remove $($v.Path)\$($v.Name): $($_.Exception.Message)" 'WARN' }
}

# ── Step 4: clear per-user Spotlight toggles across all hives ──────────────

# Older install scripts ran as SYSTEM and wrote these HKCU values into the wrong
# profile, so sweep every loaded HKEY_USERS hive plus HKCU. Deleting the value
# is the correct revert — Windows treats an absent value as the default (on).
Write-Log "Clearing per-user Spotlight toggles (ContentDeliveryManager)."
$cdmRelative = 'SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
$cdmValues   = @('RotatingLockScreenEnabled', 'RotatingLockScreenOverlayEnabled', 'SubscribedContent-338387Enabled')
$hiveRoots   = @('HKCU:')
try {
    Get-ChildItem -Path 'Registry::HKEY_USERS' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -notmatch '_Classes$' } |
        ForEach-Object { $hiveRoots += "Registry::HKEY_USERS\$($_.PSChildName)" }
} catch { }
foreach ($root in $hiveRoots) {
    $cdmPath = Join-Path $root $cdmRelative
    foreach ($name in $cdmValues) {
        try {
            if (Test-Path -LiteralPath $cdmPath) {
                Remove-ItemProperty -LiteralPath $cdmPath -Name $name -Force -ErrorAction SilentlyContinue
            }
        } catch { }
    }
}

# ── Step 5a: remove every MSI install matched BY NAME ──────────────────────

$msiFailure = $null
if (-not $productCodes) {
    Write-Log "No MSI install matched by name — MSI removal skipped (may be a Store/MSIX install; see step 5b)."
} else {
    foreach ($pc in ($productCodes | Select-Object -Unique)) {
        $msiLog = Join-Path $WorkDir "msiexec-uninstall-$($pc -replace '[^0-9A-Fa-f]','').log"
        Write-Log "Running: msiexec.exe /x $pc /qn /norestart"
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/x', $pc, '/qn', '/norestart', '/l*v', "`"$msiLog`"") -NoNewWindow -PassThru
        if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
            Write-Log "msiexec did not exit within $TimeoutSeconds seconds for $pc — killing." 'ERROR'
            try { $proc | Stop-Process -Force } catch { }
            $msiFailure = 124; continue
        }
        switch ($proc.ExitCode) {
            0     { Write-Log "msiexec $pc succeeded (exit 0)." }
            3010  { Write-Log "msiexec $pc succeeded; reboot pending (3010)." }
            1605  { Write-Log "msiexec $pc reports not installed (1605) — treating as success." }
            default { Write-Log "msiexec $pc exit $($proc.ExitCode) (see $msiLog)." 'ERROR'; $msiFailure = $proc.ExitCode }
        }
    }
}

# ── Step 5b: remove the Microsoft Store / MSIX package BY NAME ──────────────

# Store and Intune-LOB installs are MSIX, not MSI — msiexec can't touch them.
# Remove the per-user package for all users, then the provisioned copy so it
# doesn't reinstall for new profiles. All matched by package Name, no GUID.
try {
    $pkgs = Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Where-Object { $_.Name -like $AppxLike }
    foreach ($pkg in $pkgs) {
        Write-Log "Removing MSIX package (all users): $($pkg.PackageFullName)"
        try { Remove-AppxPackage -Package $pkg.PackageFullName -AllUsers -ErrorAction Stop }
        catch { try { Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction SilentlyContinue } catch { } }
    }
} catch { Write-Log "Get-AppxPackage sweep raised (non-fatal): $($_.Exception.Message)" 'WARN' }
try {
    Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like $AppxLike -or $_.PackageName -like $AppxLike } |
        ForEach-Object {
            Write-Log "Removing provisioned MSIX package: $($_.PackageName)"
            try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null } catch { }
        }
} catch { Write-Log "Get-AppxProvisionedPackage sweep raised (non-fatal): $($_.Exception.Message)" 'WARN' }

# ── Step 5c: purge SYSTEM-side residuals (scheduled tasks, HKLM bootstrap) ──

# MSI custom actions clean these for MSI-channel uninstalls. On a Store/MSIX-only
# endpoint those actions never run, leaving orphaned tasks and the bootstrap key.
# Run unconditionally and best-effort so both channels are fully reversed.
Write-Log "Purging Toast2IT scheduled tasks and HKLM bootstrap key."
try {
    Get-ScheduledTask -TaskPath '\Toast2IT\*' -ErrorAction SilentlyContinue |
        Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
    Write-Log "Scheduled tasks under \Toast2IT unregistered (or were not present)."
} catch { Write-Log "Scheduled task cleanup raised (non-fatal): $($_.Exception.Message)" 'WARN' }
try {
    if (Test-Path 'HKLM:\SOFTWARE\Toast2IT') {
        Remove-Item -Path 'HKLM:\SOFTWARE\Toast2IT' -Recurse -Force -ErrorAction SilentlyContinue
        Write-Log "HKLM:\SOFTWARE\Toast2IT removed."
    }
} catch { Write-Log "HKLM bootstrap key cleanup raised (non-fatal): $($_.Exception.Message)" 'WARN' }

# ── Step 6: purge per-user config from every profile ───────────────────────

if (-not $KeepUserConfig) {
    Write-Log "Purging per-user config (Toast2IT\Toast Notification) from all profiles."
    try {
        $usersRoot = Join-Path $env:SystemDrive 'Users'
        Get-ChildItem -Path $usersRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $cfg = Join-Path $_.FullName 'AppData\Local\Toast2IT\Toast Notification'
            if (Test-Path -LiteralPath $cfg) {
                Remove-Item -LiteralPath $cfg -Recurse -Force -ErrorAction SilentlyContinue
                Write-Log "Removed config: $cfg"
            }
        }
    } catch { Write-Log "Config purge raised (non-fatal): $($_.Exception.Message)" 'WARN' }
}

if ($msiFailure) {
    Write-Log "Toast Notification removal finished WITH a msiexec failure (exit $msiFailure). Appearance/config/MSIX steps still ran." 'ERROR'
    exit $msiFailure
}
Write-Log "Toast Notification removal complete."
exit 0
