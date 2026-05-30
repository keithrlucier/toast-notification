<#
.SYNOPSIS
    Silent install of the Toast Notification Windows agent. Designed to be
    invoked by RMM tools (NinjaOne, Datto RMM, ConnectWise Automate, Kaseya,
    Atera, etc.) against managed endpoints.

.DESCRIPTION
    1. Skips if a same-or-newer agent version is already installed.
    2. Downloads the signed MSI to ProgramData (creates folder if needed).
    3. Verifies the MSI Authenticode signature is issued to "Toast2IT, LLC"
       AND chains to a trusted root before any execution.
    4. Runs msiexec with /qn /norestart and the tenant config properties
       (CLIENTID, SERVERURL, ENROLLMENTKEY) so the agent self-registers on
       first user logon.
    5. Returns msiexec's exit code so the RMM can detect failures.

    Designed to be idempotent — safe to run on every endpoint at every
    patch cycle. If the agent is already up-to-date, the script logs and
    exits 0 without touching the system.

.PARAMETER TenantId
    Required. The tenant GUID assigned at signup. Visible at
    https://toastnotification.com/dashboard under Settings → Tenant.

.PARAMETER ServerUrl
    Required. Full URL to the Toast Notification API endpoint, e.g.
    https://toastnotification.com . The agent appends /api/... and
    /hubs/... paths internally.

.PARAMETER EnrollmentKey
    Optional. Pre-shared key required when the tenant has registration
    gating enabled (Settings → Tenant → "Require enrollment key"). Leave
    blank when gating is off. The key is base64 random opaque text — the
    agent forwards it once at registration and discards it.

.PARAMETER MsiUrl
    Optional. URL of the signed MSI. Defaults to the production hosted
    location. Set this only when running an internal mirror.

.PARAMETER WorkDir
    Optional. Local cache directory for the downloaded MSI and install
    log. Defaults to %ProgramData%\Toast2IT\Install.

.PARAMETER TimeoutSeconds
    Optional. msiexec wall-clock timeout. Default 300 (5 minutes). The
    agent install includes WindowsAppSDK so the package is ~50 MB and
    install typically completes in <60 seconds; the cushion covers slow
    endpoints and SSD lag.

.PARAMETER PinLockScreen
    Optional switch. Set ONLY when the tenant uses the Lock Screen Branding
    feature. Writes three machine-wide policy values so Windows Spotlight does
    not rotate the branded image back out:
        HKLM\...\Policies\...\Lock Screen\HideSpotlightWindowsSpotlight = 1
        HKLM\...\Policies\...\Personalization\NoLockScreenCamera        = 1
        HKLM\...\Policies\...\Personalization\LockScreenOverlaysDisabled = 1
    These are reverted by uninstall-toast-agent.ps1 (and by the MSI on
    Control-Panel uninstall). Leave this OFF for tenants that don't brand the
    lock screen — pinning Spotlight off machine-wide on those endpoints is
    needless. NOTE: this intentionally does NOT set NoCloudApplicationNotification
    (that policy suppresses toast delivery and has no place on a notification
    agent) nor the per-user ContentDeliveryManager toggles (an RMM running as
    SYSTEM writes those into the SYSTEM profile, not the user's — they are a
    no-op; the HKLM Spotlight policy above already covers the machine).

.EXAMPLE
    .\install-toast-agent.ps1 -TenantId 'a1b2c3d4-...' `
        -ServerUrl 'https://toastnotification.com' `
        -EnrollmentKey 'xQ9...'

.EXAMPLE
    # No enrollment key gating
    .\install-toast-agent.ps1 -TenantId 'a1b2c3d4-...' `
        -ServerUrl 'https://toastnotification.com'

.NOTES
    Exit codes:
       0  install completed (or agent was already at-or-above target version)
       1  parameter validation failed
       2  MSI download failed
       3  Authenticode verification failed
       4+ msiexec exit code (passed through)

    Run as SYSTEM (RMM agent context) or any user with local admin. The
    agent itself runs in the logged-on user's session — see the bundled
    Scheduled Task in the MSI.

    The Authenticode check rejects unsigned binaries AND binaries signed
    by anyone other than "Toast2IT, LLC" — protects against a malicious
    MSI substitute on the wire even if the MsiUrl host is compromised.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $TenantId,

    [Parameter(Mandatory = $true)]
    [string] $ServerUrl,

    [string] $EnrollmentKey = '',

    # Single canonical install URL — same file the dashboard's DeployCommand /
    # InstallAgent page hands to admins. Every signed-MSI ship overwrites this
    # one file on prod, so the RMM channel and the dashboard channel can never
    # drift to different versions.
    [string] $MsiUrl = 'https://toastnotification.com/downloads/ToastNotification.msi',

    [string] $WorkDir = (Join-Path $env:ProgramData 'Toast2IT\Install'),

    [int] $TimeoutSeconds = 300,

    [switch] $PinLockScreen
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

function Set-LockScreenPolicy {
    # Pins Windows Spotlight off so the agent's branded lock screen image is not
    # rotated back out. Machine-wide HKLM policy (admin context). The exact
    # inverse is in uninstall-toast-agent.ps1 / the MSI's RevertLockScreenPolicy.
    $pins = @(
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Lock Screen'; Name = 'HideSpotlightWindowsSpotlight' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization';            Name = 'NoLockScreenCamera' },
        @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization';            Name = 'LockScreenOverlaysDisabled' }
    )
    foreach ($p in $pins) {
        try {
            if (-not (Test-Path -LiteralPath $p.Path)) { [void] (New-Item -Path $p.Path -Force) }
            New-ItemProperty -LiteralPath $p.Path -Name $p.Name -Value 1 -PropertyType DWord -Force | Out-Null
            Write-Log "Pinned $($p.Path)\$($p.Name) = 1"
        } catch { Write-Log "Could not pin $($p.Path)\$($p.Name): $($_.Exception.Message)" 'WARN' }
    }
}

function Write-BootstrapFallback {
    # The MSI's WriteBootstrapJson custom action runs the freshly-dropped agent
    # exe as SYSTEM and is frequently blocked by AV/EDR on hardened endpoints
    # (msiexec logs "1721 ... a program required for this install could not be
    # run"). When that happens the MSI never writes bootstrap.json and the agent
    # has no tenant to register with. This fallback writes it directly.
    #
    # CRITICAL: keys are camelCase (tenantId/serverUrl/enrollmentKey). The agent
    # deserializes bootstrap.json case-SENSITIVELY; PascalCase silently yields an
    # empty TenantId and the device never checks in.
    $installDir    = Join-Path $env:ProgramFiles 'Toast Notification'
    $bootstrapPath = Join-Path $installDir 'bootstrap.json'
    if (-not (Test-Path -LiteralPath $installDir)) {
        Write-Log "Install dir not present — skipping bootstrap fallback." 'WARN'; return
    }
    if (Test-Path -LiteralPath $bootstrapPath) {
        Write-Log "bootstrap.json already present (MSI wrote it) — no fallback needed."; return
    }
    Write-Log "bootstrap.json missing (MSI custom action likely AV-blocked) — writing camelCase fallback."
    try {
        $obj = [ordered]@{ tenantId = $TenantId; serverUrl = $ServerUrl }
        if ($EnrollmentKey) { $obj.enrollmentKey = $EnrollmentKey }
        $json = ($obj | ConvertTo-Json -Compress)
        [System.IO.File]::WriteAllText($bootstrapPath, $json, [System.Text.Encoding]::UTF8)
        Write-Log "Wrote $bootstrapPath"
        # Restart the agent so it reads the bootstrap we just wrote (the MSI's
        # StartAgentNow already launched an instance that found no config).
        Get-Process -Name 'ToastNotification.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        & "$env:windir\System32\schtasks.exe" /Run /TN "\Toast2IT\ToastNotificationAgentLogon" | Out-Null
        Write-Log "Restarted agent via logon task to pick up bootstrap."
    } catch {
        Write-Log "Bootstrap fallback failed: $($_.Exception.Message)" 'WARN'
    }
}

# ── Step 1: validate parameters ────────────────────────────────────────────

try {
    [void] [Guid]::Parse($TenantId)
} catch {
    Write-Error "TenantId '$TenantId' is not a valid GUID."
    exit 1
}

try {
    [void] [Uri]::new($ServerUrl)
    if (-not $ServerUrl.StartsWith('https://') -and -not $ServerUrl.StartsWith('http://')) {
        throw "ServerUrl must include scheme."
    }
} catch {
    Write-Error "ServerUrl '$ServerUrl' is not a valid absolute URL."
    exit 1
}

# ── Step 2: prep work dir + log ────────────────────────────────────────────

if (-not (Test-Path -LiteralPath $WorkDir)) {
    [void] (New-Item -ItemType Directory -Path $WorkDir -Force)
}
$script:LogFile = Join-Path $WorkDir 'install-toast-agent.log'
Write-Log "Toast Notification agent installer started. WorkDir=$WorkDir"
$enrollmentKeyState = if ($EnrollmentKey) { 'set' } else { 'not set' }
Write-Log "TenantId=$TenantId ServerUrl=$ServerUrl EnrollmentKey=$enrollmentKeyState"

# ── Step 3: idempotency — check if already installed ───────────────────────

$installedVersion = $null
try {
    $uninstallKeys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    # MSI ProductName is "Toast Notification Agent" — match the prefix, not an
    # exact 'Toast Notification' (which finds nothing and breaks the same-version skip).
    $found = Get-ItemProperty -Path $uninstallKeys -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like 'Toast Notification*' } |
        Select-Object -First 1
    if ($found) {
        $installedVersion = [Version] $found.DisplayVersion
        Write-Log "Existing install detected: version $installedVersion"
    } else {
        Write-Log "No existing install detected."
    }
} catch {
    Write-Log "Could not query uninstall registry: $($_.Exception.Message)" 'WARN'
}

# ── Step 4: download the MSI ───────────────────────────────────────────────

$msiPath = Join-Path $WorkDir 'ToastNotification.Agent.msi'
Write-Log "Downloading MSI from $MsiUrl to $msiPath"

try {
    # PowerShell 5.1 ships with .NET Framework 4.x; SecurityProtocolType.Tls13
    # may not be defined on every endpoint. Tls12 alone is sufficient for
    # toastnotification.com (modern Let's Encrypt cert with TLS 1.2/1.3 cipher
    # suites). If Tls13 is available, layer it in defensively.
    $protocols = [Net.SecurityProtocolType]::Tls12
    try {
        $protocols = $protocols -bor [Net.SecurityProtocolType]::Tls13
    } catch { }
    [Net.ServicePointManager]::SecurityProtocol = $protocols

    $progressBefore = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'  # 50x faster on slow endpoints
    Invoke-WebRequest -Uri $MsiUrl -OutFile $msiPath -UseBasicParsing -TimeoutSec 120
    $ProgressPreference = $progressBefore
} catch {
    Write-Log "MSI download failed: $($_.Exception.Message)" 'ERROR'
    exit 2
}

if (-not (Test-Path -LiteralPath $msiPath) -or (Get-Item $msiPath).Length -lt 1MB) {
    Write-Log "MSI download produced no file or a truncated file." 'ERROR'
    exit 2
}
Write-Log "MSI downloaded successfully ($([math]::Round((Get-Item $msiPath).Length / 1MB, 1)) MB)."

# ── Step 5: Authenticode verification ──────────────────────────────────────

# Defense in depth — the agent itself verifies its own update binaries
# (UpdateService.IsSignedByToast2IT) but we check the install MSI here too
# so a malicious MSI can't slip through a compromised hosting domain.
$expectedSigner = 'Toast2IT, LLC'

try {
    $sig = Get-AuthenticodeSignature -FilePath $msiPath
    if ($sig.Status -ne 'Valid') {
        Write-Log "Authenticode signature status is '$($sig.Status)' — refusing to install. Message: $($sig.StatusMessage)" 'ERROR'
        exit 3
    }
    if (-not $sig.SignerCertificate -or
        -not $sig.SignerCertificate.Subject -or
        -not ($sig.SignerCertificate.Subject -like "*$expectedSigner*")) {
        Write-Log "MSI is signed by '$($sig.SignerCertificate.Subject)' — expected '$expectedSigner'. Refusing to install." 'ERROR'
        exit 3
    }
    Write-Log "Authenticode OK. Signer=$($sig.SignerCertificate.Subject), Status=Valid."
} catch {
    Write-Log "Authenticode verification raised an exception: $($_.Exception.Message)" 'ERROR'
    exit 3
}

# ── Step 6: idempotency — short-circuit if same-or-newer is installed ──────

$msiVersion = $null
try {
    # Read ProductVersion property out of the MSI without launching msiexec
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember(
        'OpenDatabase', 'InvokeMethod', $null, $installer, @($msiPath, 0))
    $view = $database.GetType().InvokeMember(
        'OpenView', 'InvokeMethod', $null, $database,
        @("SELECT Value FROM Property WHERE Property = 'ProductVersion'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    if ($record) {
        $msiVersion = [Version] $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
    }
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
} catch {
    Write-Log "Could not read MSI ProductVersion — skipping same-or-newer check. ($($_.Exception.Message))" 'WARN'
}

if ($installedVersion -and $msiVersion -and $installedVersion -ge $msiVersion) {
    Write-Log "Already at version $installedVersion (MSI is $msiVersion) — nothing to do."
    exit 0
}

# ── Step 7: msiexec install ────────────────────────────────────────────────

$msiLog = Join-Path $WorkDir 'msiexec.log'
$arguments = @(
    '/i'; "`"$msiPath`""
    "CLIENTID=`"$TenantId`""
    "SERVERURL=`"$ServerUrl`""
)
if ($EnrollmentKey) {
    $arguments += "ENROLLMENTKEY=`"$EnrollmentKey`""
}
$arguments += @('/qn'; '/norestart'; '/l*v'; "`"$msiLog`"")

Write-Log "Running: msiexec.exe $($arguments -join ' ')"

$proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments `
    -NoNewWindow -PassThru
if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
    Write-Log "msiexec did not exit within $TimeoutSeconds seconds — killing." 'ERROR'
    try { $proc | Stop-Process -Force } catch { }
    exit 124
}
$exitCode = $proc.ExitCode

# ── Step 8: report ────────────────────────────────────────────────────────

if ($exitCode -eq 0 -or $exitCode -eq 3010) {
    Write-BootstrapFallback
    if ($PinLockScreen) {
        Write-Log "PinLockScreen set — applying lock screen Spotlight policy."
        Set-LockScreenPolicy
    }
}

switch ($exitCode) {
    0     { Write-Log "msiexec succeeded (exit 0). Install complete."; exit 0 }
    3010  { Write-Log "msiexec succeeded but flagged a reboot pending (exit 3010). Treating as success."; exit 0 }
    1602  { Write-Log "msiexec was canceled (exit 1602)." 'ERROR'; exit $exitCode }
    1603  { Write-Log "msiexec fatal error (exit 1603). See $msiLog for verbose log." 'ERROR'; exit $exitCode }
    1618  { Write-Log "msiexec rejected — another install is in progress (exit 1618). Retry later." 'ERROR'; exit $exitCode }
    1638  { Write-Log "msiexec reports another version of this product is already installed (exit 1638)." 'ERROR'; exit $exitCode }
    default { Write-Log "msiexec exit code $exitCode. See $msiLog for verbose log." 'ERROR'; exit $exitCode }
}
