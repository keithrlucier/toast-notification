<#
.SYNOPSIS
    One-shot clean removal of the Toast Notification Windows agent for Tactical RMM
    (runs as SYSTEM) or a local admin. Removes BOTH channels (MSI and Store/MSIX) by
    name, and HARD-DELETES every trace of the branded lock screen, returning the
    device to the Windows default.

.DESCRIPTION
    Reverses everything the install side does, in the correct order ("do no harm"):

      1. Discover + stop the agent.
      2. Remove the product: every MSI matched by name (ProductCode read off the ARP
         key, never hardcoded) AND the Store/MSIX package + its provisioned copy.
      3. Purge SYSTEM-side residuals: \Toast2IT scheduled tasks + HKLM bootstrap key.
      4. HARD-RESET the lock screen (Invoke-ToastLockScreenReset): release HKLM
         enforcement, delete the SystemData cache slots (the selectable thumbnails),
         clear the per-user Lock Screen registry slot index across all hives, delete
         the agent image files, clear the CDM cache, and set the Windows default.
         Runs AFTER the agent is gone so it cannot re-brand; headless-safe.
      5. Purge per-user config from every profile.

    MSIX has no uninstall custom actions, so this script is the authoritative cleanup
    for both channels. Idempotent + best-effort: a missing value / absent task /
    already-removed product is success. Non-zero exits: a real msiexec error, or 3010
    when a locked lock-screen cache slot needs a reboot to finalize.

    --- LOCK-SCREEN RESET CORE: keep Invoke-ToastLockScreenReset in sync with the
        standalone Reset-ToastLockScreen.ps1 (identical logic). ASCII-only on purpose:
        a BOM-less UTF-8 .ps1 is misread by PowerShell 5.1 / RMM as CP1252. ---

.PARAMETER WorkDir
    Log + scratch directory. Defaults to %ProgramData%\Toast2IT\Install.

.PARAMETER TimeoutSeconds
    msiexec wall-clock timeout. Default 180.

.PARAMETER KeepUserConfig
    Skip the per-user config purge (step 5).

.NOTES
    Exit 0    = removal complete (or not installed).
    Exit 3010 = complete, but a locked lock-screen slot needs a reboot to finalize.
    Exit 4+   = msiexec exit code (passed through).
    Run as SYSTEM (Tactical RMM) or local admin elevated.

    RUNBOOK: the lock-screen reset clears the ENTIRE selectable lock-screen history
    (not only Toast's images) to default -- intended clean slate; the OS rebuilds on
    the next image a user picks. Verified Win10 + Win11.
#>
[CmdletBinding()]
param(
    [string] $WorkDir = (Join-Path $env:ProgramData 'Toast2IT\Install'),
    [int] $TimeoutSeconds = 180,
    [switch] $KeepUserConfig
)

$ErrorActionPreference = 'Stop'
$script:RebootNeeded = $false

if (-not (Test-Path -LiteralPath $WorkDir)) { [void](New-Item -ItemType Directory -Path $WorkDir -Force) }
$script:LogFile = Join-Path $WorkDir 'uninstall-toast-agent.log'
function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "[$((Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK'))] [$Level] $Message"
    Write-Host $line
    try { Add-Content -Path $script:LogFile -Value $line -Encoding utf8 } catch { }
}

# ============================================================================
#  LOCK-SCREEN RESET CORE  (sync with Reset-ToastLockScreen.ps1)
# ============================================================================
function Get-UserProfilePaths {
    $out = @()
    try {
        $out = Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notin @('Public','Default','Default User','All Users') } |
            Select-Object -ExpandProperty FullName
    } catch { }
    return $out
}

function ConvertTo-XmlText([string]$s) {
    return ($s -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;' -replace '"', '&quot;')
}

$script:LockScreenRelKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Lock Screen'
function Clear-LockScreenSlots([string]$hiveRoot) {
    $p = Join-Path $hiveRoot $script:LockScreenRelKey
    if (-not (Test-Path -LiteralPath $p)) { return }
    try {
        $names = (Get-Item -LiteralPath $p -ErrorAction Stop).Property |
            Where-Object { $_ -match '^(ImageId|OriginalFile|Details)_' }
        foreach ($n in $names) {
            Remove-ItemProperty -LiteralPath $p -Name $n -Force -ErrorAction SilentlyContinue
            Write-Log "  cleared $p\$n"
        }
    } catch { Write-Log "  slot sweep failed at ${p}: $($_.Exception.Message)" 'WARN' }
}

function Invoke-ToastLockScreenReset {
    param([string]$WorkDir)

    # Resolve default image (prefer img100.jpg).
    $DefaultImage = $null
    $screenDir = Join-Path $env:WINDIR 'Web\Screen'
    $preferred = Join-Path $screenDir 'img100.jpg'
    if (Test-Path -LiteralPath $preferred) { $DefaultImage = $preferred }
    else {
        $cand = Get-ChildItem -LiteralPath $screenDir -Filter 'img*.jpg' -File -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -First 1
        if ($cand) { $DefaultImage = $cand.FullName }
    }

    # 1. Release machine-wide enforcement (HKLM).
    Write-Log "LockScreen 1: releasing machine-wide enforcement (HKLM)."
    $hklm = @(
        @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'; Name = 'LockScreenImageStatus' },
        @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'; Name = 'LockScreenImagePath' },
        @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'; Name = 'LockScreenImageUrl' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'LockScreenImage' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'NoChangingLockScreen' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'NoLockScreenSlideshow' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'NoLockScreenCamera' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'LockScreenOverlaysDisabled' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Lock Screen'; Name = 'HideSpotlightWindowsSpotlight' }
    )
    foreach ($v in $hklm) {
        try {
            if (Test-Path -LiteralPath $v.Path) {
                if ($null -ne (Get-ItemProperty -LiteralPath $v.Path -Name $v.Name -ErrorAction SilentlyContinue)) {
                    Remove-ItemProperty -LiteralPath $v.Path -Name $v.Name -Force -ErrorAction SilentlyContinue
                    Write-Log "  cleared $($v.Path)\$($v.Name)"
                }
            }
        } catch { Write-Log "  could not clear $($v.Path)\$($v.Name): $($_.Exception.Message)" 'WARN' }
    }

    # 2. Hard-delete the SystemData lock-screen cache slots (all SIDs, children only).
    Write-Log "LockScreen 2: hard-deleting SystemData cache slots."
    $systemDataRoot = Join-Path $env:ProgramData 'Microsoft\Windows\SystemData'
    if (Test-Path -LiteralPath $systemDataRoot) {
        $sidDirs = Get-ChildItem -LiteralPath $systemDataRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'S-1-5-*' }
        foreach ($sidDir in $sidDirs) {
            $readOnly = Join-Path $sidDir.FullName 'ReadOnly'
            if (-not (Test-Path -LiteralPath $readOnly)) { continue }
            foreach ($slot in (Get-ChildItem -LiteralPath $readOnly -Directory -Filter 'LockScreen_*' -ErrorAction SilentlyContinue)) {
                $p = $slot.FullName
                & takeown.exe /F "$p" /R /D Y > $null 2>&1
                & icacls.exe "$p" /grant "*S-1-5-18:(OI)(CI)F" /grant "*S-1-5-32-544:(OI)(CI)F" /T /C > $null 2>&1
                try { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop; Write-Log "  deleted slot: $p" }
                catch { Write-Log "  slot locked, clears on reboot: $p" 'WARN'; $script:RebootNeeded = $true }
            }
        }
    }

    # 2b. Clear the per-user Lock Screen slot index across all hives (loaded + dormant).
    Write-Log "LockScreen 2b: clearing per-user Lock Screen slot index (all hives)."
    $loadedSids = @()
    try {
        Get-ChildItem -Path 'Registry::HKEY_USERS' -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -like 'S-1-5-21-*' -and $_.PSChildName -notmatch '_Classes$' } |
            ForEach-Object { $loadedSids += $_.PSChildName; Clear-LockScreenSlots "Registry::HKEY_USERS\$($_.PSChildName)" }
    } catch { Write-Log "  loaded-hive sweep raised: $($_.Exception.Message)" 'WARN' }
    try {
        Get-ChildItem -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList' -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -like 'S-1-5-21-*' -and $_.PSChildName -notin $loadedSids } |
            ForEach-Object {
                $sid = $_.PSChildName
                $profPath = $null
                try { $profPath = (Get-ItemProperty -LiteralPath $_.PSPath -Name 'ProfileImagePath' -ErrorAction Stop).ProfileImagePath } catch { }
                if (-not $profPath) { return }
                $dat = Join-Path $profPath 'NTUSER.DAT'
                if (-not (Test-Path -LiteralPath $dat)) { return }
                $mount = "TempToast_$sid"; $loaded = $false
                try {
                    & reg.exe load "HKU\$mount" "$dat" > $null 2>&1
                    if ($LASTEXITCODE -eq 0) { $loaded = $true; Clear-LockScreenSlots "Registry::HKEY_USERS\$mount" }
                } catch { Write-Log "  hive load failed for ${sid}: $($_.Exception.Message)" 'WARN' }
                finally { if ($loaded) { [gc]::Collect(); & reg.exe unload "HKU\$mount" > $null 2>&1 } }
            }
    } catch { Write-Log "  dormant-hive sweep raised: $($_.Exception.Message)" 'WARN' }

    # 3. Hard-delete the agent image files (all profiles, packaged + unpackaged).
    Write-Log "LockScreen 3: hard-deleting agent image files (all profiles)."
    $agentFiles = @('lockscreen.jpg','lockscreen_original.jpg','lockscreen.hash','lockscreen.jpg.tmp')
    foreach ($profilePath in (Get-UserProfilePaths)) {
        $lad = Join-Path $profilePath 'AppData\Local'
        $dirs = @(Join-Path $lad 'Toast2IT\Toast Notification')
        try {
            $dirs += Get-ChildItem -LiteralPath (Join-Path $lad 'Packages') -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like 'FileUnityCloud.ToastNotification_*' } | ForEach-Object { Join-Path $_.FullName 'LocalState' }
        } catch { }
        foreach ($dir in $dirs) {
            if (-not (Test-Path -LiteralPath $dir)) { continue }
            foreach ($f in $agentFiles) {
                $fp = Join-Path $dir $f
                try { if (Test-Path -LiteralPath $fp) { Remove-Item -LiteralPath $fp -Force -ErrorAction SilentlyContinue; Write-Log "  deleted: $fp" } } catch { }
            }
        }
    }

    # 4. Clear the per-user Content Delivery Manager image cache.
    Write-Log "LockScreen 4: clearing CDM image cache (all profiles)."
    foreach ($profilePath in (Get-UserProfilePaths)) {
        $cdm = Join-Path $profilePath 'AppData\Local\Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState'
        foreach ($sub in @('Assets','Settings')) {
            $dir = Join-Path $cdm $sub
            if (Test-Path -LiteralPath $dir) {
                try { Get-ChildItem -LiteralPath $dir -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue } catch { }
            }
        }
    }

    # 5. Reset the active image to default for the logged-on user (best-effort, observable).
    if (-not $DefaultImage) { Write-Log "LockScreen 5: no default image found; default applies at next logon." 'WARN'; return }
    $hasInteractive = $false
    try { $hasInteractive = [bool](Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue) } catch { }
    if (-not $hasInteractive) { Write-Log "LockScreen 5: no interactive session; default applies at next logon."; return }

    Write-Log "LockScreen 5: resetting active image to default via user-session WinRT task."
    $helperPath = Join-Path $WorkDir 'reset-lockscreen-winrt.ps1'
    $resultPath = Join-Path $WorkDir 'reset-lockscreen-winrt.result'
    $taskName = '\Toast2IT\ToastLockScreenResetOnce'
    $taskXmlPath = Join-Path $WorkDir 'reset-lockscreen-task.xml'
    try { Set-Content -LiteralPath $resultPath -Value '' -Force -ErrorAction SilentlyContinue } catch { }
    & icacls.exe "$resultPath" /grant "*S-1-5-32-545:(M)" > $null 2>&1
    $winrtPs = @"
`$result = '$resultPath'
try {
  Function Await(`$op, `$rt) {
    `$as = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { `$_.Name -eq 'AsTask' -and `$_.GetParameters().Count -eq 1 -and `$_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation``1' })[0]
    `$t = `$as.MakeGenericMethod(`$rt).Invoke(`$null, @(`$op))
    `$t.Wait(-1) | Out-Null
    `$t.Result
  }
  Function AwaitAction(`$action) {
    `$as = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { `$_.Name -eq 'AsTask' -and `$_.GetParameters().Count -eq 1 -and `$_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
    `$t = `$as.Invoke(`$null, @(`$action))
    `$t.Wait(-1) | Out-Null
  }
  [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime] | Out-Null
  [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
  `$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync('$DefaultImage')) ([Windows.Storage.StorageFile])
  AwaitAction ([Windows.System.UserProfile.LockScreen]::SetImageFileAsync(`$file))
  try { Set-Content -LiteralPath `$result -Value 'SET_OK' -Force } catch { }
} catch {
  try { Set-Content -LiteralPath `$result -Value ("ERR: " + `$_.Exception.Message) -Force } catch { }
}
"@
    try {
        [System.IO.File]::WriteAllText($helperPath, $winrtPs, (New-Object System.Text.UTF8Encoding($false)))
        $cmd = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
        $psArgs = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $helperPath"
        $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>Toast2IT, LLC</Author>
    <URI>$(ConvertTo-XmlText $taskName)</URI>
  </RegistrationInfo>
  <Principals>
    <Principal id="Author"><GroupId>S-1-5-32-545</GroupId><RunLevel>LeastPrivilege</RunLevel></Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <AllowHardTerminate>true</AllowHardTerminate>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <StartWhenAvailable>true</StartWhenAvailable>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
    <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$(ConvertTo-XmlText $cmd)</Command>
      <Arguments>$(ConvertTo-XmlText $psArgs)</Arguments>
      <WorkingDirectory>$(ConvertTo-XmlText $WorkDir)</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@
        [System.IO.File]::WriteAllText($taskXmlPath, $taskXml, [System.Text.Encoding]::Unicode)
        & schtasks.exe /Create /TN $taskName /XML $taskXmlPath /F > $null 2>&1
        & schtasks.exe /Run /TN $taskName > $null 2>&1
        Start-Sleep -Seconds 6
        & schtasks.exe /Delete /TN $taskName /F > $null 2>&1
        $outcome = ''
        try { $outcome = (Get-Content -LiteralPath $resultPath -Raw -ErrorAction SilentlyContinue).Trim() } catch { }
        if ($outcome -eq 'SET_OK') { Write-Log "  user-session WinRT reset succeeded." }
        elseif ($outcome) { Write-Log "  user-session WinRT reset reported: $outcome" 'WARN' }
        else { Write-Log "  user-session WinRT reset produced no result; default applies at next logon." 'WARN' }
        Remove-Item -LiteralPath $taskXmlPath, $helperPath, $resultPath -Force -ErrorAction SilentlyContinue
    } catch { Write-Log "  user-session WinRT reset failed (non-fatal): $($_.Exception.Message)" 'WARN' }
}

# ============================================================================
#  MAIN REMOVAL
# ============================================================================
Write-Log "Toast Notification removal started. WorkDir=$WorkDir"

$NameLike = 'Toast Notification*'
$AppxLike = '*ToastNotification*'

$productCodes = @()
try {
    Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like $NameLike } |
        ForEach-Object { $productCodes += $_.PSChildName; Write-Log "MSI match by name: '$($_.DisplayName)' $($_.DisplayVersion) -> $($_.PSChildName)" }
} catch { Write-Log "ARP query failed: $($_.Exception.Message)" 'WARN' }

$installPath = $null
try { $installPath = (Get-ItemProperty 'HKLM:\SOFTWARE\Toast2IT\Toast Notification' -Name 'InstallPath' -ErrorAction Stop).InstallPath } catch { }
if (-not $installPath) { $installPath = Join-Path $env:ProgramFiles 'Toast Notification' }

# 1. Stop the agent.
Write-Log "Stopping the agent if running."
try { Get-Process -Name 'ToastNotification.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue } catch { }

# 2a. Remove every MSI install matched by name.
$msiFailure = $null
if (-not $productCodes) {
    Write-Log "No MSI install matched by name (may be Store/MSIX -- see 2b)."
} else {
    foreach ($pc in ($productCodes | Select-Object -Unique)) {
        $ml = Join-Path $WorkDir "msiexec-$($pc -replace '[^0-9A-Fa-f]','').log"
        Write-Log "Running: msiexec /x $pc /qn /norestart"
        $proc = Start-Process 'msiexec.exe' -ArgumentList @('/x',$pc,'/qn','/norestart','/l*v',"`"$ml`"") -NoNewWindow -PassThru
        if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
            Write-Log "msiexec $pc hung past $TimeoutSeconds s -- killing." 'ERROR'
            try { $proc | Stop-Process -Force } catch { }
            $msiFailure = 124; continue
        }
        switch ($proc.ExitCode) {
            0 { Write-Log "msiexec $pc succeeded." }
            3010 { Write-Log "msiexec $pc succeeded; reboot pending." }
            1605 { Write-Log "msiexec $pc not installed (1605) -- treating as success." }
            default { Write-Log "msiexec $pc exit $($proc.ExitCode) (see $ml)." 'ERROR'; $msiFailure = $proc.ExitCode }
        }
    }
}

# 2b. Remove the Store / MSIX package + provisioned copy by name.
try {
    Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Where-Object { $_.Name -like $AppxLike } | ForEach-Object {
        Write-Log "Removing MSIX (all users): $($_.PackageFullName)"
        try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop }
        catch { try { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction SilentlyContinue } catch { } }
    }
} catch { Write-Log "AppxPackage sweep raised (non-fatal): $($_.Exception.Message)" 'WARN' }
try {
    Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like $AppxLike -or $_.PackageName -like $AppxLike } | ForEach-Object {
        Write-Log "Removing provisioned MSIX: $($_.PackageName)"
        try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null } catch { }
    }
} catch { Write-Log "ProvisionedPackage sweep raised (non-fatal): $($_.Exception.Message)" 'WARN' }

# 3. Purge SYSTEM-side residuals (scheduled tasks + HKLM bootstrap key).
Write-Log "Purging \Toast2IT scheduled tasks and HKLM bootstrap key."
try { Get-ScheduledTask -TaskPath '\Toast2IT\*' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue } catch { }
try { if (Test-Path 'HKLM:\SOFTWARE\Toast2IT') { Remove-Item -Path 'HKLM:\SOFTWARE\Toast2IT' -Recurse -Force -ErrorAction SilentlyContinue; Write-Log "HKLM:\SOFTWARE\Toast2IT removed." } } catch { }

# 4. HARD-RESET the lock screen (agent is gone now, so it cannot re-brand).
Write-Log "Resetting the lock screen to Windows default (hard delete)."
try { Invoke-ToastLockScreenReset -WorkDir $WorkDir } catch { Write-Log "Lock-screen reset raised (non-fatal): $($_.Exception.Message)" 'WARN' }

# 5. Purge per-user config from every profile.
if (-not $KeepUserConfig) {
    Write-Log "Purging per-user config from all profiles."
    try {
        Get-ChildItem (Join-Path $env:SystemDrive 'Users') -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $c = Join-Path $_.FullName 'AppData\Local\Toast2IT\Toast Notification'
            if (Test-Path -LiteralPath $c) { Remove-Item -LiteralPath $c -Recurse -Force -ErrorAction SilentlyContinue; Write-Log "Removed config: $c" }
        }
    } catch { Write-Log "Config purge raised (non-fatal): $($_.Exception.Message)" 'WARN' }
}

# ---- Result ----
if ($msiFailure) { Write-Log "Removal finished WITH a msiexec failure (exit $msiFailure). Lock-screen/config steps still ran." 'ERROR'; exit $msiFailure }
if ($script:RebootNeeded) { Write-Log "Removal complete; a lock-screen slot was locked -- REBOOT to finalize (exit 3010)."; exit 3010 }
Write-Log "Toast Notification removal complete."
exit 0
