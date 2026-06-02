<#
.SYNOPSIS
    Reset-ToastLockScreen.ps1 -- hard-remove EVERY trace of a Toast Notification
    (agent-applied) branded lock-screen image from a Windows endpoint and return
    the lock screen to the Windows default. Standalone, idempotent, SYSTEM-context,
    headless-safe. For use with an RMM (runs as SYSTEM) or PsExec -s -i.

.DESCRIPTION
    The agent set the lock screen with the per-user WinRT LockScreen API
    (LockScreen.SetImageFileAsync). That call makes Windows (a) COPY each image into
    a protected per-SID cache and (b) record a per-USER slot index in the registry:

        a) %ProgramData%\Microsoft\Windows\SystemData\<SID>\ReadOnly\LockScreen_*
        b) HKEY_USERS\<user-SID>\SOFTWARE\Microsoft\Windows\CurrentVersion\Lock Screen
             -> ImageId_<x> / OriginalFile_<x> / Details_<x>  (one triplet per image)

    Both are what keep old branded images selectable in Settings > Personalization >
    Lock screen, and NOTHING in the product ever cleared either -- which is why two
    branded versions survived an uninstall, and why the registry still literally
    named the agent exe + lockscreen.jpg afterward. This script closes both gaps,
    in order:

      1. Release machine-wide enforcement: delete PersonalizationCSP values and the
         Personalization / Spotlight GPO pins the installer set.
      2. HARD-DELETE the Windows SystemData lock-screen cache slots for EVERY user
         SID (takeown + icacls + delete). Deletes ONLY the LockScreen_* CHILD folders
         -- never the protected ReadOnly/<SID> parent (deleting the parent or wrecking
         its ACLs can leave the lock screen blank). Windows rebuilds the slots cleanly
         on the next image set.
      2b. Clear the per-user Lock Screen slot INDEX (ImageId_*/OriginalFile_*/Details_*)
         from every user hive -- loaded HKEY_USERS hives AND dormant profiles loaded
         via reg load / ProfileList. This is the literal branded registry trace and
         the backing index for the selectable-thumbnails strip.
      3. HARD-DELETE the agent's own lockscreen.jpg / lockscreen_original.jpg /
         lockscreen.hash from every profile, in BOTH the unpackaged (MSI) config dir
         and the packaged (Store/MSIX) LocalState dir.
      4. Clear the per-user Content Delivery Manager image cache (Spotlight belt).
      5. Reset the ACTIVE image to the genuine Windows default -- immediately for the
         logged-on user via a transient WinRT scheduled task (result logged), and
         unconditionally at next logon because the cache + index are purged.

    HEADLESS-SAFE: with no user logged on, steps 1-4 still fully clean the machine and
    the device shows the default at next logon; step 5's live refresh simply no-ops.

    EVERY step is best-effort + idempotent. The only non-zero exit is 3010 (a cache
    slot was locked because the lock screen was on display) signalling a reboot is
    needed to finalize -- wire that to an RMM reboot for a guaranteed-now result.

.PARAMETER DefaultImage
    Image to set as the post-removal default. Defaults to C:\Windows\Web\Screen\img100.jpg
    when present, else the first existing img*.jpg there.

.PARAMETER NoUserRefresh
    Skip the immediate per-user WinRT reset (step 5). The purge still yields default at
    next logon. For testing / pure-headless runs.

.PARAMETER WorkDir
    Log + scratch directory. Defaults to %ProgramData%\Toast2IT\Install.

.NOTES
    Exit 0    = clean.
    Exit 3010 = clean, but a locked cache slot needs a reboot to finalize.
    Run as SYSTEM (via an RMM) or local admin elevated.

    RUNBOOK: Step 2/2b reset the ENTIRE selectable lock-screen history (not just
    Toast's images) to default -- this is the intended "clean slate"; the OS rebuilds
    on the next image a user picks. Verified on Win10 + Win11.

    --- LOCK-SCREEN RESET CORE: keep in sync with the same block in
        uninstall-toast-agent.ps1 (the uninstall embeds an identical reset). ---
#>
[CmdletBinding()]
param(
    [string] $DefaultImage,
    [switch] $NoUserRefresh,
    [string] $WorkDir = (Join-Path $env:ProgramData 'Toast2IT\Install')
)

$ErrorActionPreference = 'Stop'
$script:RebootNeeded = $false

# -- Logging (matches the install/uninstall scripts' style) ------------------
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

# -- Resolve the default image (prefer img100.jpg, else enumerate). Reject a quoted
#    operator path -- it is interpolated into the WinRT helper literal in step 5. --
if ($DefaultImage -and ($DefaultImage -match "['""]")) {
    Write-Log "Ignoring -DefaultImage containing a quote (unsafe to interpolate): $DefaultImage" 'WARN'
    $DefaultImage = $null
}
if (-not $DefaultImage -or -not (Test-Path -LiteralPath $DefaultImage)) {
    $screenDir = Join-Path $env:WINDIR 'Web\Screen'
    $preferred = Join-Path $screenDir 'img100.jpg'
    if (Test-Path -LiteralPath $preferred) {
        $DefaultImage = $preferred
    } else {
        $candidate = Get-ChildItem -LiteralPath $screenDir -Filter 'img*.jpg' -File -ErrorAction SilentlyContinue |
            Sort-Object Name | Select-Object -First 1
        if ($candidate) { $DefaultImage = $candidate.FullName }
    }
}
if ($DefaultImage) { Write-Log "Default image target: $DefaultImage" }
else { Write-Log "No C:\Windows\Web\Screen\img*.jpg found -- relying on OS fallback for the default." 'WARN' }

# -- Helper: enumerate every real user profile dir (skips service/system profiles) --
function Get-UserProfilePaths {
    $out = @()
    try {
        $out = Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notin @('Public','Default','Default User','All Users') } |
            Select-Object -ExpandProperty FullName
    } catch { }
    return $out
}

# -- Helper: XML-escape values interpolated into the scheduled-task XML -------
function ConvertTo-XmlText([string]$s) {
    return ($s -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;' -replace '"', '&quot;')
}

# -- Helper: delete only the ImageId_/OriginalFile_/Details_ slot triplets from one
#    user hive's Lock Screen key. Never touches sibling values. ----------------
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

# ============================================================================
# STEP 1 -- Release machine-wide lock-screen enforcement (HKLM). Deleting the
# values returns control to the user / OS default. Idempotent (absent = success).
# ============================================================================
Write-Log "Step 1: releasing machine-wide lock-screen enforcement (HKLM)."
$hklmValuesToClear = @(
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
foreach ($v in $hklmValuesToClear) {
    try {
        if (Test-Path -LiteralPath $v.Path) {
            $existing = Get-ItemProperty -LiteralPath $v.Path -Name $v.Name -ErrorAction SilentlyContinue
            if ($null -ne $existing) {
                Remove-ItemProperty -LiteralPath $v.Path -Name $v.Name -Force -ErrorAction SilentlyContinue
                Write-Log "  cleared $($v.Path)\$($v.Name)"
            }
        }
    } catch { Write-Log "  could not clear $($v.Path)\$($v.Name): $($_.Exception.Message)" 'WARN' }
}

# ============================================================================
# STEP 2 -- HARD-DELETE the Windows SystemData lock-screen cache slots. Enumerate
# EVERY S-1-5-* SID; delete ONLY the LockScreen_* child folders; NEVER touch the
# ReadOnly/<SID> parent.
# ============================================================================
Write-Log "Step 2: hard-deleting SystemData lock-screen cache slots."
$systemDataRoot = Join-Path $env:ProgramData 'Microsoft\Windows\SystemData'
if (Test-Path -LiteralPath $systemDataRoot) {
    $sidDirs = Get-ChildItem -LiteralPath $systemDataRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'S-1-5-*' }
    foreach ($sidDir in $sidDirs) {
        $readOnly = Join-Path $sidDir.FullName 'ReadOnly'
        if (-not (Test-Path -LiteralPath $readOnly)) { continue }
        $slots = Get-ChildItem -LiteralPath $readOnly -Directory -Filter 'LockScreen_*' -ErrorAction SilentlyContinue
        foreach ($slot in $slots) {
            $p = $slot.FullName
            # Take ownership + grant SYSTEM/Administrators (well-known SIDs = locale-independent),
            # then delete. takeown/icacls output is noise -- swallow it.
            & takeown.exe /F "$p" /R /D Y  > $null 2>&1
            & icacls.exe  "$p" /grant "*S-1-5-18:(OI)(CI)F" /grant "*S-1-5-32-544:(OI)(CI)F" /T /C > $null 2>&1
            try {
                Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop
                Write-Log "  deleted slot: $p"
            } catch {
                # Almost always the active slot held open by LogonUI while the lock
                # screen is displayed. Cleared at reboot -- signal 3010. (Ownership was
                # taken on this slot's files; the OS resets it on the next set/reboot.)
                Write-Log "  slot locked (in use), will clear on reboot: $p -- $($_.Exception.Message)" 'WARN'
                $script:RebootNeeded = $true
            }
        }
    }
} else {
    Write-Log "  $systemDataRoot not present -- nothing to purge."
}

# ============================================================================
# STEP 2b -- Clear the per-user Lock Screen slot INDEX (ImageId_/OriginalFile_/
# Details_) from every user hive: loaded HKEY_USERS hives + dormant profiles via
# reg load / ProfileList. This removes the literal branded registry trace and the
# backing index for the selectable-thumbnails strip. Real users only (S-1-5-21-*).
# ============================================================================
Write-Log "Step 2b: clearing per-user Lock Screen slot index (all hives)."
$loadedSids = @()
try {
    Get-ChildItem -Path 'Registry::HKEY_USERS' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -like 'S-1-5-21-*' -and $_.PSChildName -notmatch '_Classes$' } |
        ForEach-Object {
            $loadedSids += $_.PSChildName
            Clear-LockScreenSlots "Registry::HKEY_USERS\$($_.PSChildName)"
        }
} catch { Write-Log "  loaded-hive sweep raised (non-fatal): $($_.Exception.Message)" 'WARN' }

# Dormant profiles: load NTUSER.DAT, clear, unload. Never unload a hive we did not load.
$profileListKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
try {
    Get-ChildItem -LiteralPath $profileListKey -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -like 'S-1-5-21-*' -and $_.PSChildName -notin $loadedSids } |
        ForEach-Object {
            $sid = $_.PSChildName
            $profPath = $null
            try { $profPath = (Get-ItemProperty -LiteralPath $_.PSPath -Name 'ProfileImagePath' -ErrorAction Stop).ProfileImagePath } catch { }
            if (-not $profPath) { return }
            $dat = Join-Path $profPath 'NTUSER.DAT'
            if (-not (Test-Path -LiteralPath $dat)) { return }
            $mount = "TempToast_$sid"
            $loaded = $false
            try {
                & reg.exe load "HKU\$mount" "$dat" > $null 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $loaded = $true
                    Clear-LockScreenSlots "Registry::HKEY_USERS\$mount"
                }
            } catch { Write-Log "  hive load failed for ${sid}: $($_.Exception.Message)" 'WARN' }
            finally {
                if ($loaded) {
                    [gc]::Collect()   # release the PS registry-provider handle before unload
                    & reg.exe unload "HKU\$mount" > $null 2>&1
                }
            }
        }
} catch { Write-Log "  dormant-hive sweep raised (non-fatal): $($_.Exception.Message)" 'WARN' }

# ============================================================================
# STEP 3 -- HARD-DELETE the agent's branded image files from every profile, in both
# the unpackaged (MSI) config dir and the packaged (Store/MSIX) LocalState dir.
# ============================================================================
Write-Log "Step 3: hard-deleting agent lock-screen image files (all profiles)."
$agentFiles = @('lockscreen.jpg', 'lockscreen_original.jpg', 'lockscreen.hash', 'lockscreen.jpg.tmp')
foreach ($profilePath in (Get-UserProfilePaths)) {
    $localAppData = Join-Path $profilePath 'AppData\Local'
    $unpackaged = Join-Path $localAppData 'Toast2IT\Toast Notification'
    $packagedRoots = @()
    try {
        $packagedRoots = Get-ChildItem -LiteralPath (Join-Path $localAppData 'Packages') -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'FileUnityCloud.ToastNotification_*' } |
            ForEach-Object { Join-Path $_.FullName 'LocalState' }
    } catch { }
    foreach ($dir in (@($unpackaged) + $packagedRoots)) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($f in $agentFiles) {
            $fp = Join-Path $dir $f
            try {
                if (Test-Path -LiteralPath $fp) {
                    Remove-Item -LiteralPath $fp -Force -ErrorAction SilentlyContinue
                    Write-Log "  deleted: $fp"
                }
            } catch { Write-Log "  could not delete ${fp}: $($_.Exception.Message)" 'WARN' }
        }
    }
}

# ============================================================================
# STEP 4 -- Clear the per-user Content Delivery Manager image cache (Spotlight
# belt-and-suspenders). Delete cached asset files only; impose no new restrictions.
# ============================================================================
Write-Log "Step 4: clearing per-user Content Delivery Manager image cache."
foreach ($profilePath in (Get-UserProfilePaths)) {
    $cdmState = Join-Path $profilePath 'AppData\Local\Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState'
    foreach ($sub in @('Assets', 'Settings')) {
        $dir = Join-Path $cdmState $sub
        if (Test-Path -LiteralPath $dir) {
            try {
                Get-ChildItem -LiteralPath $dir -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
                Write-Log "  cleared CDM $sub for $(Split-Path $profilePath -Leaf)"
            } catch { Write-Log "  CDM $sub clear raised (non-fatal): $($_.Exception.Message)" 'WARN' }
        }
    }
}

# ============================================================================
# STEP 5 -- Reset the ACTIVE image to the Windows default for the logged-on user,
# immediately, via a transient WinRT scheduled task in the interactive session.
# Best-effort + OBSERVABLE (writes a result the parent logs). If no user is logged
# on, the purged cache + index already guarantee default at next logon.
# ============================================================================
if ($NoUserRefresh) {
    Write-Log "Step 5: skipped (-NoUserRefresh). Default applies at next logon."
} elseif (-not $DefaultImage) {
    Write-Log "Step 5: skipped (no default image resolved). Default applies at next logon." 'WARN'
} else {
    $hasInteractive = $false
    try {
        $explorers = Get-CimInstance -ClassName Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue
        $hasInteractive = [bool]$explorers
    } catch { }

    if (-not $hasInteractive) {
        Write-Log "Step 5: no interactive session -- skipping live WinRT reset; default applies at next logon."
    } else {
        Write-Log "Step 5: resetting active lock screen to default via user-session WinRT task."
        $helperPath  = Join-Path $WorkDir 'reset-lockscreen-winrt.ps1'
        $resultPath  = Join-Path $WorkDir 'reset-lockscreen-winrt.result'
        $taskName    = '\Toast2IT\ToastLockScreenResetOnce'
        $taskXmlPath = Join-Path $WorkDir 'reset-lockscreen-task.xml'
        # Pre-create the result file as SYSTEM and grant the interactive user (Users)
        # modify on THAT FILE ONLY -- so the user-session helper can report back without
        # making WorkDir itself user-writable (which would let a user pre-plant the helper).
        try { Set-Content -LiteralPath $resultPath -Value '' -Force -ErrorAction SilentlyContinue } catch { }
        & icacls.exe "$resultPath" /grant "*S-1-5-32-545:(M)" > $null 2>&1

        # Helper run in the user's session. Uses the canonical Await pattern for WinRT;
        # SetImageFileAsync returns IAsyncAction (void) so it needs the non-generic awaiter.
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
            $cmd    = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
            $psArgs = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $helperPath"
            $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>Toast2IT, LLC</Author>
    <Description>One-shot: reset the lock screen to the Windows default.</Description>
    <URI>$(ConvertTo-XmlText $taskName)</URI>
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
            if ($outcome -eq 'SET_OK') { Write-Log "  user-session WinRT reset succeeded (lock screen set to default now)." }
            elseif ($outcome) { Write-Log "  user-session WinRT reset reported: $outcome (default still applies at next logon)." 'WARN' }
            else { Write-Log "  user-session WinRT reset produced no result (likely locked/disconnected session); default applies at next logon." 'WARN' }

            Remove-Item -LiteralPath $taskXmlPath, $helperPath, $resultPath -Force -ErrorAction SilentlyContinue
        } catch {
            Write-Log "  user-session WinRT reset failed (non-fatal): $($_.Exception.Message)" 'WARN'
        }
    }
}

# -- Result ------------------------------------------------------------------
if ($script:RebootNeeded) {
    Write-Log "Lock-screen reset complete; a cache slot was locked -- REBOOT to finalize (exit 3010)."
    exit 3010
}
Write-Log "Lock-screen reset complete. Device returns to the Windows default."
exit 0
