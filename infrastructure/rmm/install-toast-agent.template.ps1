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

# -- Uninstall any existing agent first ---------------------------------------
# Running /i against an already-registered ProductCode enters maintenance/
# reconfiguration mode and 1603s on custom action failures. Rollbacks from
# failed repairs corrupt the on-disk binary. Cleanest path: uninstall first
# so the subsequent install is always a fresh install with no upgrade/repair
# complexity, no rollback state, no mutex races from CA-spawned processes.
$taskRoot = "\"

$installedEntry = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' `
    -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -like '*Toast Notification*Agent*' } |
    Select-Object -First 1

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
        Write-Log "--- MSI log (first 50 lines) ---"
        Get-Content $msiLog | Select-Object -First 50 | ForEach-Object { Write-Log "  MSI: $_" "ERROR" }
        Write-Log "--- MSI log (last 50 lines) ---"
        Get-Content $msiLog | Select-Object -Last 50 | ForEach-Object { Write-Log "  MSI: $_" "ERROR" }
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
