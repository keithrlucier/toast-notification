<#
.SYNOPSIS
    One-shot, idempotent setup of git credentials for the Toast Notification
    private + public repos so push operations never trigger interactive
    Credential Manager prompts.

.DESCRIPTION
    Why this script exists: Git on Windows ships with a multi-helper chain
    (manager + store + sometimes wincred) that falls through to the GUI
    Credential Manager dialog when no helper has the right credential cached.
    With two repos under the same host (github.com/keithrlucier/{toast,
    toast-notification}) and two distinct PATs, the default "key by host"
    matching is also ambiguous — git stores ONE credential per host, so the
    second PAT silently overwrites the first.

    This script locks in the only configuration that survives both pitfalls:

      • credential.helper = store    (file-based, no GUI)
      • credential.useHttpPath = true (key credentials by full URL path, so
                                       /toast and /toast-notification each
                                       hold their own PAT)
      • GIT_TERMINAL_PROMPT = 0       (block stdin prompt fallback)
      • GCM_INTERACTIVE     = Never   (block GUI Credential Manager fallback
                                       on installs where manager remains in
                                       the helper chain at system scope)

    .git-credentials is rewritten in-place with one entry per repo (each
    written with and without the .git suffix so push and fetch both match
    the path-keyed lookup).

    Run this once whenever:
      • Keith rotates a PAT for either repo
      • A fresh dev box checks out the repo
      • Credential prompts have started to reappear (someone overrode config)

.PARAMETER PrivatePat
    GitHub PAT with repo scope on keithrlucier/toast. Required. SecureString
    so the PAT body never lands in Get-History, Start-Transcript output, or
    process-line audit logs (Sysmon, Defender for Endpoint). Pipe it in via
    Read-Host -AsSecureString or pre-build with ConvertTo-SecureString.

.PARAMETER PublicPat
    GitHub PAT with repo scope on keithrlucier/toast-notification. Required.
    See PrivatePat for the SecureString rationale.

.PARAMETER CredentialsPath
    Override the credentials file location. Defaults to ~/.git-credentials,
    which is git's built-in default for the `store` helper.

.EXAMPLE
    # Interactive — operator pastes each PAT; nothing reaches shell history.
    .\setup-git-credentials.ps1 `
        -PrivatePat (Read-Host 'Private PAT' -AsSecureString) `
        -PublicPat  (Read-Host 'Public PAT'  -AsSecureString)

.EXAMPLE
    # Programmatic — caller already has the PATs in SecureString form.
    .\setup-git-credentials.ps1 -PrivatePat $privatePatSecure -PublicPat $publicPatSecure

.NOTES
    Safe to run repeatedly. Existing credential lines for hosts other than
    github.com/keithrlucier/{toast,toast-notification} are preserved.

    PATs land plain-text inside .git-credentials by necessity — git's `store`
    helper has no other format. The SecureString gate hardens only the
    shell-side surfaces (history, transcripts, process-line audit). The
    credentials file itself lives under the user profile and is git-ignored
    everywhere it could leak.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][SecureString] $PrivatePat,
    [Parameter(Mandatory = $true)][SecureString] $PublicPat,
    [string] $CredentialsPath = (Join-Path $env:USERPROFILE '.git-credentials')
)

$ErrorActionPreference = 'Stop'

function Write-Step([string] $Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

# Unwrap SecureString -> plain text via BSTR. Works on Windows PowerShell 5.1
# AND PowerShell 7+ (ConvertFrom-SecureString -AsPlainText is PS7-only). The
# BSTR is zero-freed in finally so the plaintext doesn't linger in unmanaged
# memory beyond this function's scope.
function ConvertFrom-SecureToPlain {
    param([Parameter(Mandatory = $true)][SecureString] $Secure)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try     { [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

$privatePatPlain = ConvertFrom-SecureToPlain -Secure $PrivatePat
$publicPatPlain  = ConvertFrom-SecureToPlain -Secure $PublicPat

# ── 1. git config — reset helper chain to JUST `store`, key by full path ───
Write-Step "Resetting global credential.helper chain to file-store only"
& git config --global --replace-all credential.helper ''
& git config --global --add credential.helper store
& git config --global credential.useHttpPath true

# ── 2. env vars — block both prompt fallbacks for current user ─────────────
Write-Step "Persisting prompt-block env vars at user scope"
[Environment]::SetEnvironmentVariable('GIT_TERMINAL_PROMPT', '0',     'User')
[Environment]::SetEnvironmentVariable('GCM_INTERACTIVE',     'Never', 'User')

# ── 3. .git-credentials — rewrite Toast entries, preserve everything else ──
Write-Step "Writing $CredentialsPath"

$existing = @()
if (Test-Path $CredentialsPath) {
    $existing = Get-Content $CredentialsPath -ErrorAction SilentlyContinue
}

$pattern = [regex] '@github\.com/keithrlucier/(toast|toast-notification)(\.git)?(?:/|$)'
$preserved = $existing | Where-Object { $_ -and -not $pattern.IsMatch($_) }

$toastEntries = @(
    "https://x-access-token:$privatePatPlain@github.com/keithrlucier/toast"
    "https://x-access-token:$privatePatPlain@github.com/keithrlucier/toast.git"
    "https://x-access-token:$publicPatPlain@github.com/keithrlucier/toast-notification"
    "https://x-access-token:$publicPatPlain@github.com/keithrlucier/toast-notification.git"
)

$out = @($preserved) + $toastEntries | Where-Object { $_ }
Set-Content -Path $CredentialsPath -Value $out -Encoding ascii

# ── 4. verify ──────────────────────────────────────────────────────────────
Write-Step "Verifying configuration"
$helpers = (& git config --global --get-all credential.helper) -join ', '
$useHttp = & git config --global credential.useHttpPath
Write-Host "    credential.helper       = $helpers"
Write-Host "    credential.useHttpPath  = $useHttp"
Write-Host "    GIT_TERMINAL_PROMPT     = $([Environment]::GetEnvironmentVariable('GIT_TERMINAL_PROMPT','User'))"
Write-Host "    GCM_INTERACTIVE         = $([Environment]::GetEnvironmentVariable('GCM_INTERACTIVE','User'))"
$toastLines = (Get-Content $CredentialsPath | Where-Object { $pattern.IsMatch($_) }).Count
Write-Host "    Toast credential lines  = $toastLines (expected 4)"

Write-Host ""
Write-Host "Done. Open a new terminal so the env-var changes take effect, then" -ForegroundColor Green
Write-Host "push/pull/fetch will succeed silently on both Toast repos." -ForegroundColor Green
