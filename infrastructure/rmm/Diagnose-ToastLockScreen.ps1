<#
.SYNOPSIS
    Diagnose-ToastLockScreen.ps1 -- READ-ONLY. Dumps every place a Windows lock
    screen image can be set/cached, plus the Toast reset/uninstall logs, so we can
    see exactly what is still holding the branded lock screen after removal.
    Changes NOTHING. Run as SYSTEM (via an RMM) or an elevated admin shell and
    paste the entire output back.
.NOTES
    ASCII-only. No deletions, no registry writes, no ownership changes.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
function Section($t) { Write-Output ""; Write-Output ("==================== " + $t + " ====================") }
function Dump-RegValues($path) {
    Write-Output ("[" + $path + "]")
    try {
        if (-not (Test-Path -LiteralPath $path)) { Write-Output "  (key not present)"; return }
        $item = Get-ItemProperty -LiteralPath $path -ErrorAction Stop
        $names = (Get-Item -LiteralPath $path).Property
        if (-not $names) { Write-Output "  (no values)"; return }
        foreach ($n in $names) {
            $v = $item.$n
            if ($v -is [byte[]]) { $v = "(binary " + $v.Length + " bytes) " + (($v | Select-Object -First 64 | ForEach-Object { '{0:X2}' -f $_ }) -join '') }
            Write-Output ("  " + $n + " = " + $v)
        }
    } catch { Write-Output ("  (error reading: " + $_.Exception.Message + ")") }
}

Write-Output ("Diagnose-ToastLockScreen  |  Running as: " + [Security.Principal.WindowsIdentity]::GetCurrent().Name)
Write-Output ("OS: " + [System.Environment]::OSVersion.VersionString + "  |  " + (Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).Caption)
Write-Output ("Time: " + (Get-Date).ToString('s'))

# ---- 1. Toast script logs (what the reset/uninstall actually did) ----
Section "TOAST SCRIPT LOGS (tail)"
foreach ($lg in @('reset-toast-lockscreen.log','uninstall-toast-agent.log')) {
    $p = Join-Path $env:ProgramData ("Toast2IT\Install\" + $lg)
    Write-Output ("--- " + $p + " ---")
    if (Test-Path -LiteralPath $p) { Get-Content -LiteralPath $p -Tail 60 -ErrorAction SilentlyContinue } else { Write-Output "  (log not present)" }
}

# ---- 2. HKLM enforcement (CSP + GPO) ----
Section "HKLM ENFORCEMENT (PersonalizationCSP + Policies)"
Dump-RegValues 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'
Dump-RegValues 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
Dump-RegValues 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Lock Screen'
Dump-RegValues 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent'
Dump-RegValues 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI'
Dump-RegValues 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\Background'
Dump-RegValues 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Personalization'

# ---- 3. Per-user Lock Screen slot index + Creative (every real user hive) ----
Section "PER-USER LOCK SCREEN INDEX (HKEY_USERS S-1-5-21-*)"
$rel = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Lock Screen'
Get-ChildItem -Path 'Registry::HKEY_USERS' -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -like 'S-1-5-21-*' -and $_.PSChildName -notmatch '_Classes$' } |
    ForEach-Object {
        $sid = $_.PSChildName
        Write-Output ("--- HKU\" + $sid + " ---")
        Dump-RegValues ("Registry::HKEY_USERS\" + $sid + "\" + $rel)
        Dump-RegValues ("Registry::HKEY_USERS\" + $sid + "\" + $rel + "\Creative")
        Dump-RegValues ("Registry::HKEY_USERS\" + $sid + "\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager")
    }

# ---- 4. SystemData lock-screen cache (does it still exist?) ----
Section "SYSTEMDATA LOCK-SCREEN CACHE (ProgramData\Microsoft\Windows\SystemData)"
$sdRoot = Join-Path $env:ProgramData 'Microsoft\Windows\SystemData'
if (Test-Path -LiteralPath $sdRoot) {
    Get-ChildItem -LiteralPath $sdRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'S-1-5-*' } | ForEach-Object {
        $ro = Join-Path $_.FullName 'ReadOnly'
        Write-Output ("--- " + $ro + " ---")
        if (-not (Test-Path -LiteralPath $ro)) { Write-Output "  (no ReadOnly dir)"; return }
        try {
            $slots = Get-ChildItem -LiteralPath $ro -Directory -Filter 'LockScreen_*' -ErrorAction Stop
            if (-not $slots) { Write-Output "  (no LockScreen_* slots present -- GOOD if they were deleted)" }
            foreach ($s in $slots) {
                Write-Output ("  SLOT: " + $s.Name)
                try { Get-ChildItem -LiteralPath $s.FullName -File -ErrorAction Stop | ForEach-Object { Write-Output ("     " + $_.Name + "  (" + $_.Length + " bytes, " + $_.LastWriteTime.ToString('s') + ")") } }
                catch { Write-Output ("     (cannot list slot files: " + $_.Exception.Message + ")") }
            }
        } catch { Write-Output ("  (cannot list ReadOnly -- ACCESS: " + $_.Exception.Message + ")") }
    }
} else { Write-Output "  (SystemData not present)" }

# ---- 5. Leftover agent / brand image files anywhere obvious ----
Section "LEFTOVER lockscreen*.jpg FILES"
$searchRoots = @((Join-Path $env:SystemDrive 'Users'), (Join-Path $env:ProgramData 'Toast2IT'), (Join-Path $env:ProgramData 'Microsoft\Windows\SystemData'))
foreach ($r in $searchRoots) {
    if (Test-Path -LiteralPath $r) {
        Get-ChildItem -LiteralPath $r -Recurse -File -Include 'lockscreen*.jpg','LockScreen*.jpg' -ErrorAction SilentlyContinue |
            Select-Object -First 40 | ForEach-Object { Write-Output ("  " + $_.FullName + "  (" + $_.Length + " bytes, " + $_.LastWriteTime.ToString('s') + ")") }
    }
}

# ---- 6. Is the Toast agent actually gone? ----
Section "AGENT / PACKAGE PRESENCE"
Write-Output ("Agent process: " + (@(Get-Process -Name 'ToastNotification.Agent' -ErrorAction SilentlyContinue).Count) + " running")
Write-Output ("Install dir 'C:\Program Files\Toast Notification': " + (Test-Path 'C:\Program Files\Toast Notification'))
Write-Output ("HKLM\SOFTWARE\Toast2IT present: " + (Test-Path 'HKLM:\SOFTWARE\Toast2IT'))
try { Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*ToastNotification*' } | ForEach-Object { Write-Output ("MSIX still present: " + $_.PackageFullName) } } catch { }
try { Get-ScheduledTask -TaskPath '\Toast2IT\*' -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("Scheduled task still present: " + $_.TaskName) } } catch { }

Write-Output ""
Write-Output "==================== END DIAGNOSTIC ===================="
