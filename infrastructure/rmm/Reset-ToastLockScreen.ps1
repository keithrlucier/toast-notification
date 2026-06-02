<#
.SYNOPSIS
    Reset-ToastLockScreen.ps1 -- SURGICALLY remove a Toast-applied branded lock
    screen and restore the Windows default, WITHOUT locking the lock screen and
    WITHOUT deleting Windows' own default images. For use with an RMM (runs as
    SYSTEM) or an elevated admin shell.

.DESCRIPTION
    The agent applies the brand with the per-user WinRT LockScreen API. Windows
    records a per-user slot: HKU\<SID>\...\CurrentVersion\Lock Screen\{ImageId_<x>,
    OriginalFile_<x>, Details_<x>} plus a cache folder SystemData\<SID>\ReadOnly\
    LockScreen_<x>. The Toast slot is identifiable -- its Details_<x> names the Toast
    agent exe. This script:

      1. Re-enables normal lock-screen behavior: clears the 3 Spotlight/camera/overlay
         policy pins the installer sets, AND removes any PersonalizationCSP image a
         prior (broken) version of this script may have pinned -- so it UN-locks a box
         showing "Some of these settings are managed by your organization".
      2. Sets the active lock screen back to the Windows default (img100) via the SAME
         WinRT call the agent uses, for the logged-on user. Non-locking, no policy.
      3. SURGICALLY deletes ONLY the Toast slots (Details_<x> names the Toast agent)
         from every user hive + the matching SystemData cache folder. Windows' own
         default slots are LEFT INTACT, so the device never goes black.
      4. Deletes the agent's own lockscreen image files.

    IMPORTANT: while the Toast agent is installed AND lock-screen branding is enabled,
    the agent RE-APPLIES the brand on every startup. Run this only AFTER the agent is
    removed (uninstall) or branding is turned off in the dashboard -- otherwise the
    agent simply re-brands. NO PersonalizationCSP lock. NO mass cache deletion.

    Idempotent + best-effort. ASCII-only (PowerShell 5.1 / RMM misreads non-ASCII).

.PARAMETER NoUserRefresh
    Skip step 2 (the per-user WinRT repaint). The surgical slot removal still runs.

.PARAMETER WorkDir
    Log + scratch dir. Defaults to %ProgramData%\Toast2IT\Install.

.NOTES
    Exit 0 = clean. Exit 3010 = a Toast slot folder was locked (in use) and a reboot
    will finalize it. Run as SYSTEM (via an RMM) or local admin elevated.

    --- LOCK-SCREEN RESET CORE: keep in sync with the same block in
        uninstall-toast-agent.ps1. ---
#>
[CmdletBinding()]
param(
    [switch] $NoUserRefresh,
    [string] $WorkDir = (Join-Path $env:ProgramData 'Toast2IT\Install')
)

$ErrorActionPreference = 'Stop'
$script:RebootNeeded = $false
# Do NOT delete a brand's CACHE folder until a default is CONFIRMED set as the active
# image; otherwise a headless box could go black. Registry-triplet removal is always safe.
$script:DeferCacheDelete = $true
# A Lock Screen slot belongs to Toast when its Details_<x> (the exe that set the
# image) names the Toast agent, or its OriginalFile path references Toast.
$script:ToastSlotRegex = 'Toast2IT|ToastNotification|Toast Notification'

if (-not (Test-Path -LiteralPath $WorkDir)) { [void](New-Item -ItemType Directory -Path $WorkDir -Force) }
$script:LogFile = Join-Path $WorkDir 'reset-toast-lockscreen.log'
function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "[$((Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK'))] [$Level] $Message"
    Write-Host $line
    try { Add-Content -Path $script:LogFile -Value $line -Encoding utf8 } catch { }
}

Write-Log "Reset-ToastLockScreen started. Running as: $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Log "OS: $([System.Environment]::OSVersion.VersionString)"

# Resolve the Windows default image (prefer img100.jpg).
$DefaultImage = $null
$screenDir = Join-Path $env:WINDIR 'Web\Screen'
$preferred = Join-Path $screenDir 'img100.jpg'
if (Test-Path -LiteralPath $preferred) { $DefaultImage = $preferred }
else {
    $cand = Get-ChildItem -LiteralPath $screenDir -Filter 'img*.jpg' -File -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -First 1
    if ($cand) { $DefaultImage = $cand.FullName }
}
if ($DefaultImage) { Write-Log "Default image: $DefaultImage" } else { Write-Log "No Web\Screen\img*.jpg default found." 'WARN' }

function Get-UserProfilePaths {
    $out = @()
    try {
        $out = Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notin @('Public','Default','Default User','All Users') } |
            Select-Object -ExpandProperty FullName
    } catch { }
    return $out
}

# Take ownership of and delete ONE SystemData LockScreen_<letter> slot folder for a SID.
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

# Surgically remove Toast slots from ONE user hive. $Sid is the real user SID (for SystemData);
# $HiveRoot is the registry root to read (Registry::HKEY_USERS\<sid-or-mount>).
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
            $cand = $Matches[1]   # capture the slot letter BEFORE the content -match clobbers $Matches
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

# ============================================================================
# STEP 1 -- Re-enable normal lock-screen behavior. Clear the install's Spotlight
# pins AND remove any PersonalizationCSP image a prior version pinned (un-lock
# "managed by your organization"). NO new policy is written.
# ============================================================================
Write-Log "Step 1: clearing lock-screen policy pins + any prior CSP lock (HKLM)."
$hklmClear = @(
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Lock Screen'; Name = 'HideSpotlightWindowsSpotlight' },
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'NoLockScreenCamera' },
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'LockScreenOverlaysDisabled' },
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'LockScreenImage' },
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'; Name = 'NoChangingLockScreen' },
    # Undo any PersonalizationCSP image a prior (broken) version of this script pinned.
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
    } catch { Write-Log "  could not clear $($v.Path)\$($v.Name): $($_.Exception.Message)" 'WARN' }
}

# ============================================================================
# STEP 2 -- Set the active lock screen back to the Windows default for the logged-on
# user via the same per-user WinRT call the agent uses (non-locking). Best-effort.
# ============================================================================
if ($NoUserRefresh) {
    Write-Log "Step 2: skipped (-NoUserRefresh)."
} elseif (-not $DefaultImage) {
    Write-Log "Step 2: skipped (no default image)." 'WARN'
} else {
    $hasInteractive = $false
    try { $hasInteractive = [bool](Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue) } catch { }
    if (-not $hasInteractive) {
        Write-Log "Step 2: no interactive session; default applies after the brand slot is removed + next lock."
    } else {
        Write-Log "Step 2: setting active lock screen to default via user-session WinRT task."
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
            else { Write-Log "  WinRT reset produced no result (locked/disconnected session)." 'WARN' }
            Remove-Item -LiteralPath $taskXmlPath, $helperPath, $resultPath -Force -ErrorAction SilentlyContinue
        } catch { Write-Log "  WinRT reset failed (non-fatal): $($_.Exception.Message)" 'WARN' }
    }
}

# ============================================================================
# STEP 3 -- SURGICALLY remove ONLY Toast slots (Details names the Toast agent) from
# every real-user hive (loaded HKEY_USERS + dormant via reg load) + the matching
# SystemData cache folder. Windows defaults are left intact (no black).
# ============================================================================
Write-Log "Step 3: surgically removing Toast lock-screen slots (Toast-only)."
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
            finally { if ($loaded) { [gc]::Collect(); & reg.exe unload "HKU\$mount" > $null 2>&1 } }
        }
} catch { Write-Log "  dormant-hive sweep raised: $($_.Exception.Message)" 'WARN' }

# ============================================================================
# STEP 4 -- Delete the agent's own lockscreen image files (all profiles).
# ============================================================================
Write-Log "Step 4: deleting agent lock-screen image files (all profiles)."
$agentFiles = @('lockscreen.jpg', 'lockscreen_original.jpg', 'lockscreen.hash', 'lockscreen.jpg.tmp')
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

# -- Result ------------------------------------------------------------------
if ($script:RebootNeeded) {
    Write-Log "Reset complete; a Toast cache slot was locked -- REBOOT to finalize (exit 3010)."
    exit 3010
}
Write-Log "Reset complete. Toast brand removed; Windows defaults left intact."
exit 0
