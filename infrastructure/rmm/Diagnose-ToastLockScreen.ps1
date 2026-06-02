<#
.SYNOPSIS
    Diagnose-ToastLockScreen.ps1 -- READ-ONLY. Dumps every place a Windows lock
    screen image can be set / cached / enforced, the Toast reset/uninstall logs,
    AND the Toast agent's own log, so we can see what is enforcing or blocking the
    lock screen. Changes NOTHING. Run as SYSTEM (via an RMM) or an elevated admin
    shell and paste the entire output back.
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
            if ($v -is [byte[]]) {
                # Show binary as both hex (short) and any embedded ASCII path (e.g. OriginalFile IDList).
                $ascii = -join ($v | ForEach-Object { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { '.' } })
                $v = "(binary " + $v.Length + "B) ascii='" + $ascii + "'"
            }
            Write-Output ("  " + $n + " = " + $v)
        }
    } catch { Write-Output ("  (error reading: " + $_.Exception.Message + ")") }
}

Write-Output ("Diagnose-ToastLockScreen  |  Running as: " + [Security.Principal.WindowsIdentity]::GetCurrent().Name)
Write-Output ("OS: " + [System.Environment]::OSVersion.VersionString + "  |  " + (Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).Caption)
Write-Output ("Time: " + (Get-Date).ToString('s'))

# ---- 1. WHO is managing the lock screen (CSP + GPO) -- the lock culprit ----
Section "LOCK-SCREEN ENFORCEMENT (CSP + GPO) -- is anything LOCKING it?"
Dump-RegValues 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'
Dump-RegValues 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
Dump-RegValues 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Lock Screen'
Dump-RegValues 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Personalization'

# ---- 2. The Toast AGENT log (why can the fresh install not brand?) ----
Section "TOAST AGENT LOG (lock-screen lines)"
$agentLogs = @()
$searchDirs = @()
try { Get-ChildItem -LiteralPath (Join-Path $env:SystemDrive 'Users') -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $searchDirs += (Join-Path $_.FullName 'AppData\Local\Toast2IT\Toast Notification')
        Get-ChildItem -LiteralPath (Join-Path $_.FullName 'AppData\Local\Packages') -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'FileUnityCloud.ToastNotification_*' } | ForEach-Object { $searchDirs += (Join-Path $_.FullName 'LocalState') }
    } } catch { }
$searchDirs += (Join-Path $env:ProgramData 'Toast2IT')
foreach ($d in $searchDirs) {
    if (-not (Test-Path -LiteralPath $d)) { continue }
    Get-ChildItem -LiteralPath $d -Filter '*.log' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch 'reset-toast|uninstall-toast|msiexec' } | ForEach-Object { $agentLogs += $_.FullName }
}
if (-not $agentLogs) { Write-Output "  (no agent log found)" }
foreach ($lg in ($agentLogs | Select-Object -Unique)) {
    Write-Output ("--- " + $lg + " ---")
    Get-Content -LiteralPath $lg -ErrorAction SilentlyContinue | Where-Object { $_ -match 'LockScreen|lock screen|SetImage|appearance|Appearance|GPO|policy|denied|0x8' } | Select-Object -Last 30
}

# ---- 3. Toast reset/uninstall script logs (tail) ----
Section "TOAST SCRIPT LOGS (tail)"
foreach ($lg in @('reset-toast-lockscreen.log','uninstall-toast-agent.log')) {
    $p = Join-Path $env:ProgramData ("Toast2IT\Install\" + $lg)
    Write-Output ("--- " + $p + " ---")
    if (Test-Path -LiteralPath $p) { Get-Content -LiteralPath $p -Tail 25 -ErrorAction SilentlyContinue } else { Write-Output "  (log not present)" }
}

# ---- 4. Per-user Lock Screen slot index (ALL real-user hives, incl. Entra) ----
Section "PER-USER LOCK SCREEN INDEX (HKEY_USERS) -- which slots are Toast vs default?"
$rel = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Lock Screen'
Get-ChildItem -Path 'Registry::HKEY_USERS' -ErrorAction SilentlyContinue |
    Where-Object { ($_.PSChildName -like 'S-1-5-21-*' -or $_.PSChildName -like 'S-1-12-1-*') -and $_.PSChildName -notmatch '_Classes$' } |
    ForEach-Object {
        Write-Output ("--- HKU\" + $_.PSChildName + " ---")
        Dump-RegValues ("Registry::HKEY_USERS\" + $_.PSChildName + "\" + $rel)
    }

# ---- 5. SystemData lock-screen cache (ALL SIDs incl. Entra S-1-12-1) ----
Section "SYSTEMDATA LOCK-SCREEN CACHE (ALL SIDs) -- which images are cached?"
$sdRoot = Join-Path $env:ProgramData 'Microsoft\Windows\SystemData'
if (Test-Path -LiteralPath $sdRoot) {
    Get-ChildItem -LiteralPath $sdRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'S-1-*' } | ForEach-Object {
        $ro = Join-Path $_.FullName 'ReadOnly'
        Write-Output ("--- " + $ro + " ---")
        if (-not (Test-Path -LiteralPath $ro)) { Write-Output "  (no ReadOnly dir)"; return }
        try {
            $slots = Get-ChildItem -LiteralPath $ro -Directory -Filter 'LockScreen_*' -ErrorAction Stop
            if (-not $slots) { Write-Output "  (no LockScreen_* slots)" }
            foreach ($s in $slots) {
                $main = Get-ChildItem -LiteralPath $s.FullName -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notmatch '_\d+_\d+' } | Select-Object -First 1
                $sz = if ($main) { "$($main.Length)B $($main.LastWriteTime.ToString('s'))" } else { "?" }
                Write-Output ("  " + $s.Name + "  ->  " + $sz)
            }
        } catch { Write-Output ("  (cannot list -- ACCESS: " + $_.Exception.Message + ")") }
    }
} else { Write-Output "  (SystemData not present)" }

# ---- 6. Agent / package presence ----
Section "AGENT / PACKAGE PRESENCE"
Write-Output ("Agent process: " + (@(Get-Process -Name 'ToastNotification.Agent' -ErrorAction SilentlyContinue).Count) + " running")
Write-Output ("Install dir 'C:\Program Files\Toast Notification': " + (Test-Path 'C:\Program Files\Toast Notification'))
Write-Output ("HKLM\SOFTWARE\Toast2IT present: " + (Test-Path 'HKLM:\SOFTWARE\Toast2IT'))
try { Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*ToastNotification*' } | ForEach-Object { Write-Output ("MSIX present: " + $_.PackageFullName) } } catch { }

Write-Output ""
Write-Output "==================== END DIAGNOSTIC ===================="
