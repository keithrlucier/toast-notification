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
$f = "$env:TEMP\ToastNotification.msi"
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

# -- (Optional, informational) verify the publisher --------------------------
try {
    $sig = Get-AuthenticodeSignature -FilePath $f
    Write-Log "Authenticode: Status=$($sig.Status) Signer=$($sig.SignerCertificate.Subject)"
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*Toast2IT, LLC*') {
        Write-Log "MSI signature is not a Valid Toast2IT, LLC signature -- review before trusting." "WARN"
    }
} catch {
    Write-Log "Authenticode check could not run: $_" "WARN"
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

# -- MSI Install --------------------------------------------------------------
$msiexec = if ([Environment]::Is64BitProcess) {
    "$env:windir\System32\msiexec.exe"
} else {
    "$env:windir\Sysnative\msiexec.exe"
}

$msiLog = "$logDir\ToastNotification_msi_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
Write-Log "Using msiexec: $msiexec"
Write-Log "Starting MSI install. MSI log: $msiLog"

$proc = Start-Process $msiexec -ArgumentList "/i `"$f`" /qn /norestart /l*v `"$msiLog`" CLIENTID=$TenantId SERVERURL=$ServerUrl ENROLLMENTKEY=$EnrollmentKey" -Wait -PassThru

Write-Log "MSI exit code: $($proc.ExitCode)"

# -- Cleanup MSI --------------------------------------------------------------
Remove-Item $f -Force -ErrorAction SilentlyContinue
Write-Log "MSI file removed"

# -- Handle result ------------------------------------------------------------
$msiSuccess = $false
if ($proc.ExitCode -eq 0 -or $proc.ExitCode -eq 3010) {
    if ($proc.ExitCode -eq 3010) {
        Write-Log "MSI install complete. Reboot recommended but not required."
    } else {
        Write-Log "MSI install complete"
    }
    $msiSuccess = $true
} else {
    Write-Log "MSI install failed with exit code $($proc.ExitCode)" "ERROR"
    if (Test-Path $msiLog) {
        Write-Log "--- Relevant MSI log entries ---"
        $p = "2203|error|failed|rollback|1603|1619|1721"
        $hits = Get-Content $msiLog | Where-Object { $_ -match $p } | Select-Object -Last 30
        foreach ($hit in $hits) {
            Write-Log "  MSI: $hit" "ERROR"
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
    exit $proc.ExitCode
}

# -- Start agent --------------------------------------------------------------
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
