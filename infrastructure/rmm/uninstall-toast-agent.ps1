<#
.SYNOPSIS
    One-shot clean removal of the Toast Notification Windows agent, for use with an
    RMM (runs as SYSTEM) or a local admin. Removes BOTH channels (MSI and Store/MSIX)
    by name, then SURGICALLY removes the branded lock screen and restores the Windows
    default -- without locking the lock screen or deleting Windows' own defaults.

.DESCRIPTION
    Order ("do no harm"):
      1. Discover + stop the agent.
      2. Remove the product: every MSI matched by name + the Store/MSIX package and its
         provisioned copy. (Agent gone first, so it cannot re-brand mid-clean.)
      3. Purge SYSTEM-side residuals: \Toast2IT scheduled tasks + HKLM bootstrap key.
      4. Lock-screen reset (Invoke-ToastLockScreenReset): clear the install's policy pins
         + any prior PersonalizationCSP lock; set the active image to the Windows default
         via the same WinRT call the agent uses; SURGICALLY delete ONLY the Toast slots
         (Details names the Toast agent) from every user hive + the matching SystemData
         cache folder -- Windows defaults left intact (no black, no policy lock).
      5. Purge per-user config from every profile.

    MSIX has no uninstall custom actions, so this script is the authoritative cleanup
    for both channels. Idempotent + best-effort. ASCII-only (PowerShell 5.1 / RMM).

.PARAMETER WorkDir       Log + scratch dir. Default %ProgramData%\Toast2IT\Install.
.PARAMETER TimeoutSeconds  msiexec timeout. Default 180.
.PARAMETER KeepUserConfig  Skip the per-user config purge.

.NOTES
    Exit 0 = complete. Exit 3010 = complete but a Toast cache slot was locked; reboot
    finalizes it. Exit 4+ = msiexec exit code. Run as SYSTEM (via an RMM) or admin.

    --- LOCK-SCREEN RESET CORE: keep Invoke-ToastLockScreenReset in sync with the
        standalone Reset-ToastLockScreen.ps1. ---
#>
[CmdletBinding()]
param(
    [string] $WorkDir = (Join-Path $env:ProgramData 'Toast2IT\Install'),
    [int] $TimeoutSeconds = 180,
    [switch] $KeepUserConfig
)

$ErrorActionPreference = 'Stop'
$script:RebootNeeded = $false
# Do NOT delete a brand's CACHE folder until a default is CONFIRMED set as the active
# image; otherwise a headless box could go black. Registry-triplet removal is always safe.
$script:DeferCacheDelete = $true
$script:ToastSlotRegex = 'Toast2IT|ToastNotification|Toast Notification'

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

function Remove-ToastSlotFolder {
    param([string]$Sid, [string]$Letter)
    $slot = Join-Path $env:ProgramData ("Microsoft\Windows\SystemData\$Sid\ReadOnly\LockScreen_$Letter")
    if (-not (Test-Path -LiteralPath $slot)) { return }
    if ($script:DeferCacheDelete) { Write-Log "  deferring cache slot delete to reboot (no confirmed default yet): $slot" 'WARN'; $script:RebootNeeded = $true; return }
    & takeown.exe /F "$slot" /R /D Y > $null 2>&1
    & icacls.exe "$slot" /grant "*S-1-5-18:(OI)(CI)F" /grant "*S-1-5-32-544:(OI)(CI)F" /T /C > $null 2>&1
    try { Remove-Item -LiteralPath $slot -Recurse -Force -ErrorAction Stop; Write-Log "  deleted cache slot: $slot" }
    catch { Write-Log "  cache slot locked (in use), clears on reboot: $slot" 'WARN'; $script:RebootNeeded = $true }
}

function Remove-ToastSlotsFromHive {
    param([string]$HiveRoot, [string]$Sid)
    $key = Join-Path $HiveRoot 'SOFTWARE\Microsoft\Windows\CurrentVersion\Lock Screen'
    if (-not (Test-Path -LiteralPath $key)) { return }
    try {
        $item  = Get-ItemProperty -LiteralPath $key -ErrorAction Stop
        $props = (Get-Item -LiteralPath $key).Property
    } catch { return }
    $brandLetters = @()
    foreach ($n in $props) {
        $letter = $null
        if ($n -match '^Details_(.+)$') {
            $cand = $Matches[1]   # capture letter BEFORE the content -match clobbers $Matches
            if (([string]$item.$n) -match $script:ToastSlotRegex) { $letter = $cand }
        } elseif ($n -match '^OriginalFile_(.+)$') {
            $cand = $Matches[1]
            $v = $item.$n
            if ($v -is [byte[]]) {
                $ascii = -join ($v | ForEach-Object { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { ' ' } })
                if ($ascii -match $script:ToastSlotRegex) { $letter = $cand }
            }
        }
        if ($letter) { $brandLetters += $letter }
    }
    foreach ($letter in ($brandLetters | Select-Object -Unique)) {
        if ($letter -notmatch '^[A-Za-z0-9]{1,4}$') { Write-Log "  skipping non-standard slot id '$letter'" 'WARN'; continue }
        foreach ($vn in @("ImageId_$letter", "OriginalFile_$letter", "Details_$letter")) {
            Remove-ItemProperty -LiteralPath $key -Name $vn -Force -ErrorAction SilentlyContinue
        }
        Write-Log "  removed Toast slot index '$letter' (SID $Sid)"
        Remove-ToastSlotFolder -Sid $Sid -Letter $letter
    }
}

function Invoke-ToastLockScreenReset {
    param([string]$WorkDir)

    $DefaultImage = $null
    $screenDir = Join-Path $env:WINDIR 'Web\Screen'
    $preferred = Join-Path $screenDir 'img100.jpg'
    if (Test-Path -LiteralPath $preferred) { $DefaultImage = $preferred }
    else {
        $cand = Get-ChildItem -LiteralPath $screenDir -Filter 'img*.jpg' -File -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -First 1
        if ($cand) { $DefaultImage = $cand.FullName }
    }

    # 1. Clear install policy pins + any prior PersonalizationCSP lock (un-lock 'managed by org').
    Write-Log "LockScreen 1: clearing policy pins + any prior CSP lock."
    $hklmClear = @(
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Lock Screen'; Name = 'HideSpotlightWindowsSpotlight' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'NoLockScreenCamera' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'LockScreenOverlaysDisabled' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'LockScreenImage' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'NoChangingLockScreen' },
        @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'; Name = 'LockScreenImageStatus' },
        @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'; Name = 'LockScreenImagePath' },
        @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'; Name = 'LockScreenImageUrl' }
    )
    foreach ($v in $hklmClear) {
        try {
            if ((Test-Path -LiteralPath $v.Path) -and ($null -ne (Get-ItemProperty -LiteralPath $v.Path -Name $v.Name -ErrorAction SilentlyContinue))) {
                Remove-ItemProperty -LiteralPath $v.Path -Name $v.Name -Force -ErrorAction SilentlyContinue
                Write-Log "  cleared $($v.Path)\$($v.Name)"
            }
        } catch { }
    }

    # 2. Set the active image to the Windows default via the per-user WinRT call (non-locking).
    if ($DefaultImage) {
        $hasInteractive = $false
        try { $hasInteractive = [bool](Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue) } catch { }
        if (-not $hasInteractive) {
            Write-Log "LockScreen 2: no interactive session; default applies after slot removal + next lock."
        } else {
            Write-Log "LockScreen 2: setting active image to default via user-session WinRT task."
            $helperPath = Join-Path $WorkDir 'reset-lockscreen-winrt.ps1'
            $resultPath = Join-Path $WorkDir 'reset-lockscreen-winrt.result'
            $taskName   = '\Toast2IT\ToastLockScreenResetOnce'
            $taskXmlPath = Join-Path $WorkDir 'reset-lockscreen-task.xml'
            try { Set-Content -LiteralPath $resultPath -Value '' -Force -ErrorAction SilentlyContinue } catch { }
            & icacls.exe "$resultPath" /grant "*S-1-5-32-545:(M)" > $null 2>&1
            $winrtPs = @"
`$result = '$resultPath'
try {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction SilentlyContinue
  Function Await(`$op, `$rt) {
    `$as = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { `$_.Name -eq 'AsTask' -and `$_.GetParameters().Count -eq 1 -and `$_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation``1' })[0]
    `$t = `$as.MakeGenericMethod(`$rt).Invoke(`$null, @(`$op)); `$t.Wait(-1) | Out-Null; `$t.Result
  }
  Function AwaitAction(`$action) {
    `$as = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { `$_.Name -eq 'AsTask' -and `$_.GetParameters().Count -eq 1 -and `$_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
    `$t = `$as.Invoke(`$null, @(`$action)); `$t.Wait(-1) | Out-Null
  }
  [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime] | Out-Null
  [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
  `$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync('$DefaultImage')) ([Windows.Storage.StorageFile])
  AwaitAction ([Windows.System.UserProfile.LockScreen]::SetImageFileAsync(`$file))
  try { Set-Content -LiteralPath `$result -Value 'SET_OK' -Force } catch { }
} catch { try { Set-Content -LiteralPath `$result -Value ("ERR: " + `$_.Exception.Message) -Force } catch { } }
"@
            try {
                [System.IO.File]::WriteAllText($helperPath, $winrtPs, (New-Object System.Text.UTF8Encoding($false)))
                $cmd = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
                $psArgs = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$helperPath`""
                $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Author>Toast2IT, LLC</Author><URI>$taskName</URI></RegistrationInfo>
  <Principals><Principal id="A"><GroupId>S-1-5-32-545</GroupId><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><AllowHardTerminate>true</AllowHardTerminate><AllowStartOnDemand>true</AllowStartOnDemand><StartWhenAvailable>true</StartWhenAvailable><Enabled>true</Enabled><Hidden>true</Hidden><UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine><ExecutionTimeLimit>PT2M</ExecutionTimeLimit></Settings>
  <Actions Context="A"><Exec><Command>$cmd</Command><Arguments>$psArgs</Arguments><WorkingDirectory>$WorkDir</WorkingDirectory></Exec></Actions>
</Task>
"@
                [System.IO.File]::WriteAllText($taskXmlPath, $taskXml, [System.Text.Encoding]::Unicode)
                & schtasks.exe /Create /TN $taskName /XML $taskXmlPath /F > $null 2>&1
                & schtasks.exe /Run /TN $taskName > $null 2>&1
                Start-Sleep -Seconds 6
                & schtasks.exe /Delete /TN $taskName /F > $null 2>&1
                $outcome = ''
                try { $outcome = (Get-Content -LiteralPath $resultPath -Raw -ErrorAction SilentlyContinue).Trim() } catch { }
                if ($outcome -eq 'SET_OK') { Write-Log "  active lock screen set to default."; $script:DeferCacheDelete = $false }
                elseif ($outcome) { Write-Log "  WinRT reset reported: $outcome" 'WARN' }
                else { Write-Log "  WinRT reset produced no result." 'WARN' }
                Remove-Item -LiteralPath $taskXmlPath, $helperPath, $resultPath -Force -ErrorAction SilentlyContinue
            } catch { Write-Log "  WinRT reset failed (non-fatal): $($_.Exception.Message)" 'WARN' }
        }
    }

    # 3. Surgically remove ONLY Toast slots (loaded HKEY_USERS + dormant hives).
    Write-Log "LockScreen 3: surgically removing Toast lock-screen slots."
    $loadedSids = @()
    try {
        Get-ChildItem -Path 'Registry::HKEY_USERS' -ErrorAction SilentlyContinue |
            Where-Object { ($_.PSChildName -like 'S-1-5-21-*' -or $_.PSChildName -like 'S-1-12-1-*') -and $_.PSChildName -notmatch '_Classes$' } |
            ForEach-Object { $loadedSids += $_.PSChildName; Remove-ToastSlotsFromHive -HiveRoot "Registry::HKEY_USERS\$($_.PSChildName)" -Sid $_.PSChildName }
    } catch { Write-Log "  loaded-hive sweep raised: $($_.Exception.Message)" 'WARN' }
    try {
        Get-ChildItem -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList' -ErrorAction SilentlyContinue |
            Where-Object { ($_.PSChildName -like 'S-1-5-21-*' -or $_.PSChildName -like 'S-1-12-1-*') -and $_.PSChildName -notin $loadedSids } |
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
                    if ($LASTEXITCODE -eq 0) { $loaded = $true; Remove-ToastSlotsFromHive -HiveRoot "Registry::HKEY_USERS\$mount" -Sid $sid }
                } catch { Write-Log "  hive load failed for ${sid}: $($_.Exception.Message)" 'WARN' }
                finally {
                    if ($loaded) {
                        # Drop the PS registry-provider key handles the hive read cached, then run
                        # pending finalizers so reg.exe can unload. [gc]::Collect() alone does NOT
                        # wait for finalizers, so the unload could fail silently and leave NTUSER.DAT
                        # mounted under HKU\TempToast_<sid> (locked until reboot, blocking that
                        # profile's next logon). Check the result and retry once.
                        [gc]::Collect(); [gc]::WaitForPendingFinalizers(); [gc]::Collect()
                        & reg.exe unload "HKU\$mount" > $null 2>&1
                        if ($LASTEXITCODE -ne 0) {
                            Start-Sleep -Milliseconds 250
                            [gc]::Collect(); [gc]::WaitForPendingFinalizers()
                            & reg.exe unload "HKU\$mount" > $null 2>&1
                            if ($LASTEXITCODE -ne 0) { Write-Log "  reg unload failed for ${sid} (hive stays mounted until reboot)." 'WARN' }
                        }
                    }
                }
            }
    } catch { Write-Log "  dormant-hive sweep raised: $($_.Exception.Message)" 'WARN' }

    # 4. Delete the agent's own lockscreen image files (all profiles).
    Write-Log "LockScreen 4: deleting agent image files (all profiles)."
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
        # Materialize the SafeHandle BEFORE waiting. On Windows PowerShell 5.1 a
        # Start-Process -PassThru object whose Handle was never touched returns $null
        # from .ExitCode after the process exits -- which would make every uninstall
        # (even a FAILED msiexec /x) fall through to the 'default' branch with a $null
        # exit, silently recording SUCCESS on a broken removal. Caching the handle here
        # is what makes $proc.ExitCode reliable below.
        $null = $proc.Handle
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

# 4. Lock-screen reset (agent is gone now, so it cannot re-brand).
Write-Log "Removing the branded lock screen + restoring the Windows default."
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
if ($script:RebootNeeded) { Write-Log "Removal complete; a Toast cache slot was locked -- REBOOT to finalize (exit 3010)."; exit 3010 }
Write-Log "Toast Notification removal complete."
exit 0
