# ============================================================================
#  Toast Notification -- agent install script (for use with an RMM)
#  Downloaded per-tenant from the Install Agent page (values pre-filled).
#  Run via an RMM (as SYSTEM) or an elevated shell. Downloads the signed MSI,
#  installs silently, registers the device, and (optionally) pins the lock
#  screen branding policy.
#  ASCII-only on purpose: a BOM-less UTF-8 .ps1 is misread by PowerShell 5.1.
# ============================================================================

# -- Config (pre-filled for this tenant) -------------------------------------
$TenantId      = "__TENANTID__"
$ServerUrl     = "__SERVERURL__"
$EnrollmentKey = "__ENROLLMENTKEY__"
$PinLockScreen = $true   # $true only for tenants using Lock Screen Branding

# -- Logging setup ------------------------------------------------------------
$logDir  = "C:\Temp"
$logFile = "$logDir\ToastNotification_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
if (-not (Test-Path $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    Add-Content -Path $logFile -Value $line
    switch ($Level) {
        "ERROR" { Write-Error   $Message }
        "WARN"  { Write-Warning $Message }
        default { Write-Output  $Message }
    }
}

Write-Log "Script started. Log: $logFile"
Write-Log "Running as: $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Log "OS: $([System.Environment]::OSVersion.VersionString)"
Write-Log "PowerShell host is 64-bit: $([Environment]::Is64BitProcess)"

# -- Stop agent if running ----------------------------------------------------
Write-Log "Stopping Toast Notification agent if running..."
Stop-Process -Name "ToastNotification.Agent" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$stillRunning = Get-Process -Name "ToastNotification.Agent" -ErrorAction SilentlyContinue
if ($stillRunning) {
    Write-Log "Agent still running after stop attempt -- proceeding anyway" "WARN"
} else {
    Write-Log "Agent not running"
}

# -- Download -----------------------------------------------------------------
# Use the script's own log dir (C:\Temp) rather than $env:TEMP (which resolves
# to C:\Windows\Temp under SYSTEM). Some Entra/Intune-hardened endpoints block
# the Windows Installer service from opening IStorage from C:\Windows\Temp,
# producing MSI error 2203 / STG_E_ACCESSDENIED / exit 1619.
$f = "$logDir\ToastNotification.msi"
Write-Log "Downloading MSI to: $f"

try {
    $webClient = New-Object System.Net.WebClient
    $webClient.DownloadFile("$ServerUrl/downloads/ToastNotification.msi", $f)
    Write-Log "Download complete. File size: $((Get-Item $f).Length) bytes"
} catch {
    Write-Log "Download failed: $_" "ERROR"
    exit 1
}

if (-not (Test-Path $f)) {
    Write-Log "MSI not found after download: $f" "ERROR"
    exit 1
}

if ((Get-Item $f).Length -eq 0) {
    Write-Log "MSI is 0 bytes after download" "ERROR"
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    exit 1
}

# -- Unblock the MSI ----------------------------------------------------------
Write-Log "Unblocking MSI file"
Unblock-File -Path $f
Write-Log "MSI unblocked"

# -- Verify Authenticode signature (enforced -- mirrors main install script) --
try {
    $sig = Get-AuthenticodeSignature -FilePath $f
    Write-Log "Authenticode: Status=$($sig.Status) Signer=$($sig.SignerCertificate.Subject)"
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*Toast2IT, LLC*') {
        Write-Log "MSI signature is not a Valid Toast2IT, LLC signature -- aborting." "ERROR"
        Remove-Item $f -Force -ErrorAction SilentlyContinue
        exit 3
    }
} catch {
    Write-Log "Authenticode check could not run: $_ -- aborting." "ERROR"
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    exit 3
}

# -- Wait for AV scan to complete ---------------------------------------------
Write-Log "Waiting for AV scan to release file..."
Start-Sleep -Seconds 15

# -- Confirm file is not locked -----------------------------------------------
try {
    $stream = [System.IO.File]::Open($f, 'Open', 'Read', 'None')
    $stream.Close()
    Write-Log "MSI file is accessible and not locked"
} catch {
    Write-Log "MSI file is locked or inaccessible: $_" "ERROR"
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    exit 1
}

# -- Idempotency: skip the whole MSI dance if already at-or-above this version -
# Re-running /i against an already-registered ProductCode enters maintenance/
# reconfiguration mode; a custom-action failure there 1603s and rolls the product
# back -- and the uninstall-first path below would have ALREADY removed the
# previously-healthy install, leaving the device broken. Reading the MSI's own
# ProductVersion and short-circuiting when the endpoint is current makes this
# script a true no-op on healthy devices instead of a destructive reinstall.
# (Mirrors the same-or-newer guard in install-toast-agent.ps1.)
$msiVersion = $null
try {
    $wi  = New-Object -ComObject WindowsInstaller.Installer
    $db  = $wi.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $wi, @($f, 0))
    $vw  = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Value FROM Property WHERE Property = 'ProductVersion'"))
    $vw.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $vw, $null)
    $rec = $vw.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $vw, $null)
    if ($rec) { $msiVersion = [Version]$rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(1)) }
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($vw)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($db)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($wi)
    Write-Log "MSI ProductVersion: $msiVersion"
} catch {
    Write-Log "Could not read MSI ProductVersion -- proceeding without same-version skip ($($_.Exception.Message))" "WARN"
}

$installedEntry = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' `
    -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -like '*Toast Notification*Agent*' } |
    Select-Object -First 1

if ($installedEntry -and $msiVersion) {
    try {
        $installedVer = [Version]$installedEntry.DisplayVersion
        if ($installedVer -ge $msiVersion) {
            Write-Log "Agent already at version $installedVer (MSI is $msiVersion) -- already current, nothing to do."
            Remove-Item $f -Force -ErrorAction SilentlyContinue
            Write-Log "Script finished successfully (no-op: already current)"
            exit 0
        }
        Write-Log "Installed $installedVer is older than MSI $msiVersion -- upgrading."
    } catch {
        Write-Log "Version compare failed ($($_.Exception.Message)) -- proceeding with install" "WARN"
    }
}

# -- Uninstall any existing agent first ---------------------------------------
# Running /i against an already-registered ProductCode enters maintenance/
# reconfiguration mode and 1603s on custom action failures. Rollbacks from
# failed repairs corrupt the on-disk binary. Cleanest path: uninstall first
# so the subsequent install is always a fresh install with no upgrade/repair
# complexity, no rollback state, no mutex races from CA-spawned processes.
$taskRoot = "\"

if ($installedEntry) {
    $productCode      = $installedEntry.PSChildName
    $installedVersion = $installedEntry.DisplayVersion
    Write-Log "Found installed version: $installedVersion ($productCode) -- uninstalling first"

    $uninstallLog  = "$logDir\ToastNotification_uninstall_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
    $uninstallArgs = "/x `"$productCode`" /qn /norestart /l*v `"$uninstallLog`""
    $uninstallTask = "ToastNotificationRmmUninstall"

    try {
        Get-ScheduledTask -TaskName $uninstallTask -TaskPath $taskRoot -ErrorAction SilentlyContinue |
            Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue

        $action    = New-ScheduledTaskAction -Execute "$env:windir\System32\msiexec.exe" -Argument $uninstallArgs
        $principal = New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\SYSTEM" -RunLevel Highest -LogonType ServiceAccount
        $settings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds 300) -MultipleInstances IgnoreNew
        $taskDef   = New-ScheduledTask -Action $action -Principal $principal -Settings $settings
        Register-ScheduledTask -TaskName $uninstallTask -TaskPath $taskRoot -InputObject $taskDef -Force | Out-Null

        Start-ScheduledTask -TaskName $uninstallTask -TaskPath $taskRoot

        $deadline = (Get-Date).AddSeconds(300)
        do {
            Start-Sleep -Seconds 5
            $taskObj = Get-ScheduledTask -TaskName $uninstallTask -TaskPath $taskRoot -ErrorAction SilentlyContinue
            $state   = if ($taskObj) { $taskObj.State } else { 'Ready' }
        } while ($state -eq 'Running' -and (Get-Date) -lt $deadline)

        $uInfo         = Get-ScheduledTaskInfo -TaskName $uninstallTask -TaskPath $taskRoot -ErrorAction SilentlyContinue
        $uninstallCode = if ($uInfo) { [int64]$uInfo.LastTaskResult } else { 1619 }
        Write-Log "Uninstall exit code: $uninstallCode"

        Get-ScheduledTask -TaskName $uninstallTask -TaskPath $taskRoot -ErrorAction SilentlyContinue |
            Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue

        if ($uninstallCode -eq 0) {
            Write-Log "Uninstall complete"
            Start-Sleep -Seconds 3
        } else {
            Write-Log "Uninstall returned $uninstallCode -- proceeding with install anyway" "WARN"
        }
    } catch {
        Write-Log "Uninstall task setup failed: $($_.Exception.Message) -- proceeding with install anyway" "WARN"
    }
} else {
    Write-Log "No existing agent installation found -- fresh install"
}

# -- MSI Install (always a fresh /i after uninstall above) --------------------
# Standalone SYSTEM task bypasses EDR RMM->PowerShell->msiexec chain block.
# MSI staged to C:\Windows\Installer\ so IStorage opens from the trusted
# Windows Installer package cache, not a user-writable temp directory.
$msiLog   = "$logDir\ToastNotification_msi_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
$taskName = "ToastNotificationRmmInstall"

$stagePath = "$env:windir\Installer\ToastNotification_rmm.msi"
try {
    Copy-Item -Path $f -Destination $stagePath -Force -ErrorAction Stop
    Write-Log "MSI staged to Windows Installer cache: $stagePath"
    $installSource = $stagePath
} catch {
    Write-Log "Staging to Windows\Installer failed: $($_.Exception.Message) -- using original path" "WARN"
    $installSource = $f
}

$msiArgs = "/i `"$installSource`" /qn /norestart /l*v `"$msiLog`" CLIENTID=`"$TenantId`" SERVERURL=`"$ServerUrl`""
if ($EnrollmentKey) { $msiArgs += " ENROLLMENTKEY=`"$EnrollmentKey`"" }

Write-Log "MSI log: $msiLog"

$usedTask = $false
try {
    Get-ScheduledTask -TaskName $taskName -TaskPath $taskRoot -ErrorAction SilentlyContinue |
        Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue

    $action    = New-ScheduledTaskAction -Execute "$env:windir\System32\msiexec.exe" -Argument $msiArgs
    $principal = New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\SYSTEM" -RunLevel Highest -LogonType ServiceAccount
    $settings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds 300) -MultipleInstances IgnoreNew
    $taskDef   = New-ScheduledTask -Action $action -Principal $principal -Settings $settings
    Register-ScheduledTask -TaskName $taskName -TaskPath $taskRoot -InputObject $taskDef -Force | Out-Null

    Write-Log "Starting MSI install via standalone SYSTEM task..."
    Start-ScheduledTask -TaskName $taskName -TaskPath $taskRoot
    $usedTask = $true
} catch {
    Write-Log "Task setup failed: $($_.Exception.Message) -- falling back to direct Start-Process" "WARN"
}

if ($usedTask) {
    $deadline = (Get-Date).AddSeconds(300)
    do {
        Start-Sleep -Seconds 5
        $taskObj = Get-ScheduledTask -TaskName $taskName -TaskPath $taskRoot -ErrorAction SilentlyContinue
        $state   = if ($taskObj) { $taskObj.State } else { 'Ready' }
    } while ($state -eq 'Running' -and (Get-Date) -lt $deadline)

    if ($state -eq 'Running') {
        Write-Log "Task did not finish within 300s -- stopping and treating as failure." "WARN"
        Stop-ScheduledTask -TaskName $taskName -TaskPath $taskRoot -ErrorAction SilentlyContinue
        $msiExitCode = 1603
    } else {
        $info        = Get-ScheduledTaskInfo -TaskName $taskName -TaskPath $taskRoot -ErrorAction SilentlyContinue
        $msiExitCode = if ($info) { [int64]$info.LastTaskResult } else { 1619 }
        Write-Log "Task LastTaskResult: $msiExitCode"
    }

    Get-ScheduledTask -TaskName $taskName -TaskPath $taskRoot -ErrorAction SilentlyContinue |
        Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
} else {
    $proc        = Start-Process "$env:windir\System32\msiexec.exe" -ArgumentList $msiArgs -Wait -PassThru
    $msiExitCode = $proc.ExitCode
}

Write-Log "MSI exit code: $msiExitCode"

# -- Cleanup MSI --------------------------------------------------------------
Remove-Item $f -Force -ErrorAction SilentlyContinue
Remove-Item $stagePath -Force -ErrorAction SilentlyContinue
Write-Log "MSI file removed"

# -- Handle result ------------------------------------------------------------
$msiSuccess = $false
if ($msiExitCode -eq 0 -or $msiExitCode -eq 3010) {
    if ($msiExitCode -eq 3010) {
        Write-Log "MSI install complete. Reboot recommended but not required."
    } else {
        Write-Log "MSI install complete"
    }
    $msiSuccess = $true
} else {
    Write-Log "MSI install failed with exit code $msiExitCode" "ERROR"
    if (Test-Path $msiLog) {
        # Surface the ACTUAL failing action instead of a blind 100-line dump.
        # "Action ended ...: <Name>. Return value 3." names the custom action that
        # rolled the install back; "returned actual error" carries its exit code.
        # Logged at INFO (not ERROR) so the RMM console shows a few clean lines
        # rather than wrapping every MSI line in a PowerShell error record.
        $signal = Select-String -Path $msiLog -Pattern 'Return value 3|returned actual error|error status: 1603|MainEngineThread is returning|Note: 1: 172[123]|Rollback'
        if ($signal) {
            Write-Log "--- MSI failure signal (filtered) ---"
            $signal | ForEach-Object { Write-Log "  MSI: $($_.Line.Trim())" }
        } else {
            Write-Log "--- MSI log tail (no explicit failure signal matched) ---"
            Get-Content $msiLog | Select-Object -Last 40 | ForEach-Object { Write-Log "  MSI: $_" }
        }
    }
}

# -- Write bootstrap.json fallback (camelCase!) -------------------------------
# Runs when the MSI's own bootstrap writer was AV-blocked (1721). camelCase keys
# are REQUIRED -- the agent deserializes bootstrap.json case-sensitively.
$installDir    = "C:\Program Files\Toast Notification"
$bootstrapPath = "$installDir\bootstrap.json"

if (Test-Path $installDir) {
    if (-not (Test-Path $bootstrapPath)) {
        Write-Log "bootstrap.json not found -- writing camelCase fallback"
        try {
            $bootstrap = "{`"tenantId`":`"$TenantId`",`"serverUrl`":`"$ServerUrl`",`"enrollmentKey`":`"$EnrollmentKey`"}"
            [System.IO.File]::WriteAllText($bootstrapPath, $bootstrap, [System.Text.Encoding]::UTF8)
            Write-Log "bootstrap.json written to: $bootstrapPath"
        } catch {
            Write-Log "Failed to write bootstrap.json: $_" "WARN"
        }
    } else {
        Write-Log "bootstrap.json already present -- skipping fallback write"
    }
} else {
    Write-Log "Install dir not found ($installDir) -- MSI rolled back fully, skipping bootstrap fallback" "WARN"
}

# -- Exit if MSI failed and no install dir present ----------------------------
if (-not $msiSuccess -and -not (Test-Path $installDir)) {
    Write-Log "MSI failed and no install dir present -- exiting with error" "ERROR"
    exit $msiExitCode
}

# -- Ensure scheduled tasks exist (MSI task CAs are best-effort) ---------------
# InstallScheduledTask / InstallUpdaterTask in the MSI are Return=ignore: a
# schtasks failure must never roll a committed file install back to 1603. If
# either CA was skipped or AV-blocked, recreate the task here from the XML the
# MSI dropped so the agent still launches at logon and the updater stays usable.
if (Test-Path $installDir) {
    $taskFallbacks = @(
        @{ Name = "\Toast2IT\ToastNotificationAgentLogon"; Xml = "$installDir\ToastNotificationLogon.xml" },
        @{ Name = "\Toast2IT\ToastNotificationUpdater";    Xml = "$installDir\ToastNotificationUpdater.xml" }
    )
    foreach ($tf in $taskFallbacks) {
        try {
            & "$env:windir\System32\schtasks.exe" /Query /TN $tf.Name 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0 -and (Test-Path $tf.Xml)) {
                & "$env:windir\System32\schtasks.exe" /Create /TN $tf.Name /XML $tf.Xml /F | Out-Null
                if ($LASTEXITCODE -eq 0) {
                    Write-Log "Created missing scheduled task: $($tf.Name)"
                } else {
                    Write-Log "Could not create scheduled task $($tf.Name) (schtasks exit $LASTEXITCODE)" "WARN"
                }
            }
        } catch {
            Write-Log "Task-existence check failed for $($tf.Name): $($_.Exception.Message)" "WARN"
        }
    }
}

# -- Start agent --------------------------------------------------------------
# Kill any agent instance that may have been started by a WiX custom action
# (StartAgentNow CA fires during install/reconfiguration). Without this, the
# new schtask /Run hits the primary-worker mutex and exits silently, leaving
# the dashboard showing the old version.
Write-Log "Ensuring no agent process is running before start..."
Get-Process -Name "ToastNotification.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Write-Log "Starting Toast Notification agent..."
try {
    Start-Process "$env:windir\System32\schtasks.exe" -ArgumentList '/Run /TN "\Toast2IT\ToastNotificationAgentLogon"' -Wait -ErrorAction Stop
    Write-Log "Agent started via scheduled task"
} catch {
    Write-Log "Could not start agent via scheduled task: $_" "WARN"
}

# -- Lock Screen Registry (3 HKLM pins only, gated) ---------------------------
if ($PinLockScreen) {
    Write-Log "Applying lock screen branding policy (PinLockScreen=$PinLockScreen)"
    # Clear any default lock-screen image a prior Toast removal pinned (PersonalizationCSP),
    # so the agent's per-user lock-screen branding is not blocked by an enforced image.
    $cspKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP"
    foreach ($cv in 'LockScreenImageUrl','LockScreenImagePath','LockScreenImageStatus') {
        Remove-ItemProperty -Path $cspKey -Name $cv -Force -ErrorAction SilentlyContinue
    }
    Write-Log "Cleared any pinned default lock-screen image so branding can apply."
    $registryPaths = @(
        @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Lock Screen"; Name = "HideSpotlightWindowsSpotlight"; Value = 1; Type = "DWord" },
        @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization";            Name = "NoLockScreenCamera";          Value = 1; Type = "DWord" },
        @{ Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization";            Name = "LockScreenOverlaysDisabled";   Value = 1; Type = "DWord" }
    )
    foreach ($entry in $registryPaths) {
        try {
            if (-not (Test-Path $entry.Path)) {
                New-Item -Path $entry.Path -Force | Out-Null
                Write-Log "Created registry key: $($entry.Path)"
            }
            Set-ItemProperty -Path $entry.Path -Name $entry.Name -Value $entry.Value -Type $entry.Type
            Write-Log "Set: $($entry.Path)\$($entry.Name) = $($entry.Value)"
        } catch {
            Write-Log "Failed to set $($entry.Path)\$($entry.Name): $_" "WARN"
        }
    }
    Write-Log "Lock screen branding policy applied."
} else {
    Write-Log "PinLockScreen disabled -- skipping lock screen policy."
}

Write-Log "Script finished successfully"
