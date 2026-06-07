<#
.SYNOPSIS
    Single-command, mispackaging-proof release pipeline for the Toast Notification agent.

.DESCRIPTION
    Closes CR-P1-003 / DEP-004 / FIX-BUILD-VERSION-001. The 2026-06-05 fleet incident
    (a 0.4.38 binary shipped inside a 0.4.40 MSI) happened because the release was a
    sequence of separate commands: build-msi.ps1 -Version stamped ONLY the WiX
    ProductVersion (the outer wrapper), while the agent binary version came independently
    from ToastRevival.Agent.csproj. Nothing asserted the two agreed, and build-msi.ps1
    re-runs dotnet publish on every call, so running it after signing clobbered the signed
    exe.

    This script makes the whole release one immutable pass:
      1. STAMP all three version surfaces from a single -Version (drift becomes impossible):
           - src/ToastRevival.Agent/ToastRevival.Agent.csproj  (Version/AssemblyVersion/FileVersion -> 3-part)
           - src/ToastRevival.Api/appsettings.json              (Agent:LatestVersion           -> 3-part)
           - src/ToastRevival.Agent/Package.appxmanifest        (Identity Version              -> 4-part)
      2. PUBLISH the self-contained agent ONCE.
      3. ASSERT the published binary FileVersion == -Version  (the FIX-BUILD-VERSION-001 guard).
      4. SIGN the exe on the Thales SafeNet token (skipped with -SkipSigning).
      5. WiX build the MSI directly against the (signed) publish dir --NEVER re-publishes.
      6. SIGN the MSI (skipped with -SkipSigning).
      7. VERIFY: extract the MSI (msiexec /a), prove the inner exe is byte-identical to the
         compiled exe, prove the MSI ProductVersion == 4-part, and (when signed) prove both
         the MSI and the inner exe carry a Valid Authenticode signature from the expected cert.
      8. EMIT a machine-readable release manifest (versions, hashes, signer, URLs).

    MSIX (Microsoft Store) is intentionally NOT part of this pipeline: it ships unsigned and is
    re-signed by the Store, on a separate cadence. Use scripts/build-msix.ps1 for that.

.PARAMETER Version
    3-part agent version, e.g. "0.4.43". The 4-part MSI/MSIX form ("0.4.43.0") is derived.

.PARAMETER SkipSigning
    Produce an UNSIGNED MSI and skip all signature checks. For dry-runs / clean-workspace
    verification on a box with no token. The emitted manifest is marked signed=false and is
    NOT for distribution.

.PARAMETER Intune
    After signing, also wrap the signed MSI into a .intunewin via build-intunewin.ps1.
    Requires signing (incompatible with -SkipSigning, since Intune rejects unsigned MSIs).

.PARAMETER RuntimeIdentifier
    Publish RID. Default win-x64.

.EXAMPLE
    # Real signed release (Thales token plugged in + unlocked; PIN dialog will pop twice):
    .\scripts\release.ps1 -Version 0.4.43

.EXAMPLE
    # Dry-run on a box with no token --proves the whole pipeline end-to-end:
    .\scripts\release.ps1 -Version 0.4.43 -SkipSigning
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Version,

    [switch] $SkipSigning,
    [switch] $Intune,
    [string] $RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# 0. Validate inputs, derive version forms, resolve paths.
# ---------------------------------------------------------------------------
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Version must be 3-part with no leading zeros (e.g. 0.4.43). Got: '$Version'. The 4-part MSI/MSIX form is derived automatically."
}
if ($Intune -and $SkipSigning) {
    throw "-Intune requires a signed MSI; it cannot be combined with -SkipSigning (Intune rejects unsigned MSIs on managed endpoints)."
}

$V3 = $Version            # 3-part: csproj + appsettings
$V4 = "$Version.0"        # 4-part: MSI ProductVersion + MSIX Identity

$repoRoot       = Split-Path -Parent $PSScriptRoot
$projectPath    = Join-Path $repoRoot "src\ToastRevival.Agent\ToastRevival.Agent.csproj"
$agentSrcDir    = Join-Path $repoRoot "src\ToastRevival.Agent"
$csprojPath     = $projectPath
$appsettingsPath = Join-Path $repoRoot "src\ToastRevival.Api\appsettings.json"
$manifestPath   = Join-Path $repoRoot "src\ToastRevival.Agent\Package.appxmanifest"
$publishDir     = Join-Path $repoRoot "artifacts\ToastRevival.Agent\$RuntimeIdentifier-self-contained"
$installerSrc   = Join-Path $repoRoot "installer\ToastRevival.Agent.Setup.wxs"
$logonTaskXml   = Join-Path $repoRoot "installer\ToastNotificationLogon.xml"
$updaterTaskXml = Join-Path $repoRoot "installer\ToastNotificationUpdater.xml"
$licenseRtf     = Join-Path $repoRoot "installer\License.rtf"
$installerOut   = Join-Path $repoRoot "artifacts\installer"
$msiPath        = Join-Path $installerOut "ToastNotification.Agent-$V4.msi"
$manifestOut    = Join-Path $installerOut "ToastNotification.Agent-$V4.release.json"
$exeName        = "ToastNotification.Agent.exe"
$publishedExe   = Join-Path $publishDir $exeName

# ToastNotificationHealth: the LocalSystem phone-home service, a SECOND signed exe
# shipped inside the same MSI. Published self-contained (multi-file, untrimmed) to
# its own dir so its runtime DLLs never collide with the agent's.
$healthProject      = Join-Path $repoRoot "src\ToastRevival.AgentHealthService\ToastRevival.AgentHealthService.csproj"
$healthCsprojPath   = $healthProject
$healthPublishDir   = Join-Path $repoRoot "artifacts\ToastRevival.AgentHealthService\$RuntimeIdentifier-self-contained"
$healthExeName      = "ToastNotificationHealth.exe"
$healthPublishedExe = Join-Path $healthPublishDir $healthExeName

# WSEC-L2 / sign-msix.ps1: the one cert allowed to sign Toast artifacts.
$expectedThumbprint = "19B07B46712C2D87FF6AA99842F7EF6B036FEDA7"

foreach ($p in @($csprojPath, $appsettingsPath, $manifestPath, $installerSrc, $logonTaskXml, $updaterTaskXml, $licenseRtf, $healthCsprojPath)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Required source not found: $p" }
}

Write-Host ""
Write-Host "================ Toast Notification release ================" -ForegroundColor Cyan
Write-Host "  Version (3-part) : $V3"
Write-Host "  Version (4-part) : $V4"
Write-Host "  Runtime          : $RuntimeIdentifier"
Write-Host "  Signing          : $(if ($SkipSigning) { 'SKIPPED (dry-run, unsigned)' } else { 'Thales SafeNet token (PIN dialog will pop)' })"
Write-Host "  Intune wrap      : $(if ($Intune) { 'yes' } else { 'no' })"
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# UTF-8-no-BOM file I/O. PowerShell's Get-Content -Raw reads a BOM-less UTF-8
# file as CP1252 and Set-Content writes a BOM + double-encodes multi-byte
# chars --that round-trip corrupts source files. Read/write through
# System.IO.File with an explicit no-BOM UTF8Encoding to stay byte-clean.
# ---------------------------------------------------------------------------
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Read-Utf8([string]$Path)  { [System.IO.File]::ReadAllText($Path, $utf8NoBom) }
function Write-Utf8([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom) }

# Replace the FIRST capture-group-2 span with $NewValue. Throws if the pattern
# never matches, so an un-bumped or renamed version line fails loud instead of
# silently shipping a stale version (the exact FIX-BUILD-VERSION-001 hole).
function Set-VersionSpan {
    param(
        [string] $Text,
        [string] $Pattern,
        [string] $NewValue,
        [string] $Label,
        [System.Text.RegularExpressions.RegexOptions] $Options = [System.Text.RegularExpressions.RegexOptions]::None
    )
    $rx = [regex]::new($Pattern, $Options)
    if (-not $rx.IsMatch($Text)) { throw "Version-stamp target not found: $Label" }
    # '${1}' / '${2}' are .NET regex group refs; braces disambiguate '${1}0...' from group 10.
    return $rx.Replace($Text, ('${1}' + $NewValue + '${2}'), 1)
}

# ---------------------------------------------------------------------------
# 1. Stamp all three version surfaces from the single -Version.
# ---------------------------------------------------------------------------
Write-Host "==> [1/8] Stamping version surfaces ($V3 / $V4)" -ForegroundColor Cyan

$csproj = Read-Utf8 $csprojPath
$csproj = Set-VersionSpan $csproj '(<Version>)[^<]*(</Version>)'                 $V3 'csproj <Version>'
$csproj = Set-VersionSpan $csproj '(<AssemblyVersion>)[^<]*(</AssemblyVersion>)' $V3 'csproj <AssemblyVersion>'
$csproj = Set-VersionSpan $csproj '(<FileVersion>)[^<]*(</FileVersion>)'         $V3 'csproj <FileVersion>'
Write-Utf8 $csprojPath $csproj

# Health service csproj: same three version surfaces, same single -Version.
$healthCsproj = Read-Utf8 $healthCsprojPath
$healthCsproj = Set-VersionSpan $healthCsproj '(<Version>)[^<]*(</Version>)'                 $V3 'health csproj <Version>'
$healthCsproj = Set-VersionSpan $healthCsproj '(<AssemblyVersion>)[^<]*(</AssemblyVersion>)' $V3 'health csproj <AssemblyVersion>'
$healthCsproj = Set-VersionSpan $healthCsproj '(<FileVersion>)[^<]*(</FileVersion>)'         $V3 'health csproj <FileVersion>'
Write-Utf8 $healthCsprojPath $healthCsproj

$appsettings = Read-Utf8 $appsettingsPath
$appsettings = Set-VersionSpan $appsettings '("LatestVersion"\s*:\s*")[^"]*(")' $V3 'appsettings Agent:LatestVersion'
Write-Utf8 $appsettingsPath $appsettings

# The <Identity ...> tag spans multiple lines; Singleline lets the lazy [^>] run
# across newlines so it stops at the FIRST Version= attribute inside that one tag.
$manifest = Read-Utf8 $manifestPath
$manifest = Set-VersionSpan $manifest '(<Identity\b[^>]*?\bVersion=")[^"]*(")' $V4 'manifest Identity Version' ([System.Text.RegularExpressions.RegexOptions]::Singleline)
Write-Utf8 $manifestPath $manifest

# Read each surface back and assert it now carries the target --never trust the write blind.
$csprojCheck      = Read-Utf8 $csprojPath
$appsettingsCheck = Read-Utf8 $appsettingsPath
$manifestCheck    = Read-Utf8 $manifestPath
if ($csprojCheck -notmatch [regex]::Escape("<Version>$V3</Version>"))             { throw "csproj <Version> did not stamp to $V3" }
if ($csprojCheck -notmatch [regex]::Escape("<AssemblyVersion>$V3</AssemblyVersion>")) { throw "csproj <AssemblyVersion> did not stamp to $V3" }
if ($csprojCheck -notmatch [regex]::Escape("<FileVersion>$V3</FileVersion>"))     { throw "csproj <FileVersion> did not stamp to $V3" }
if ($appsettingsCheck -notmatch ('"LatestVersion"\s*:\s*"' + [regex]::Escape($V3) + '"')) { throw "appsettings Agent:LatestVersion did not stamp to $V3" }
if ($manifestCheck -notmatch ('<Identity\b[^>]*?\bVersion="' + [regex]::Escape($V4) + '"'))          { throw "manifest Identity Version did not stamp to $V4" }
$healthCsprojCheck = Read-Utf8 $healthCsprojPath
if ($healthCsprojCheck -notmatch [regex]::Escape("<FileVersion>$V3</FileVersion>")) { throw "health csproj <FileVersion> did not stamp to $V3" }
Write-Host "    csproj + appsettings + health -> $V3 ; manifest -> $V4   (all verified)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 2. Publish the self-contained agent ONCE.
# ---------------------------------------------------------------------------
Write-Host "==> [2/8] Publishing self-contained agent ($RuntimeIdentifier)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $projectPath `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -p:SatelliteResourceLanguages=en `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
if (-not (Test-Path $publishedExe)) { throw "Published exe not found: $publishedExe" }

# Strip non-English satellite locale folders (matches build-msi.ps1).
$stripped = 0
Get-ChildItem $publishDir -Directory |
    Where-Object { $_.Name -match '^\w{2,3}(-\w+)*$' -and $_.Name -notlike 'en*' } |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force; $stripped++ }
if ($stripped -gt 0) { Write-Host "    Stripped $stripped non-English locale folders." }

# Publish the health service alongside the agent (self-contained, multi-file,
# untrimmed — same robustness posture as the agent; the WiX <Files> glob harvests
# whatever this emits into HEALTHFOLDER).
Write-Host "    Publishing ToastNotificationHealth ($RuntimeIdentifier)" -ForegroundColor Cyan
if (Test-Path $healthPublishDir) { Remove-Item $healthPublishDir -Recurse -Force }
dotnet publish $healthProject `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:SatelliteResourceLanguages=en `
    --output $healthPublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (health service) failed (exit $LASTEXITCODE)" }
if (-not (Test-Path $healthPublishedExe)) { throw "Published health exe not found: $healthPublishedExe" }
Get-ChildItem $healthPublishDir -Directory |
    Where-Object { $_.Name -match '^\w{2,3}(-\w+)*$' -and $_.Name -notlike 'en*' } |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force }

# ---------------------------------------------------------------------------
# 3. THE GUARD (FIX-BUILD-VERSION-001): published binary version must == -Version.
# ---------------------------------------------------------------------------
Write-Host "==> [3/8] Asserting published binary version == $V3" -ForegroundColor Cyan
$builtFileVersion = (Get-Item $publishedExe).VersionInfo.FileVersion
# FileVersion may report 3- or 4-part ("0.4.43" or "0.4.43.0"); compare the first 3 octets.
$builtV3 = (($builtFileVersion -split '\.')[0..2]) -join '.'
if ($builtV3 -ne $V3) {
    throw "BINARY VERSION MISMATCH: published $exeName FileVersion is '$builtFileVersion' (=> $builtV3) but release is $V3. This is the exact 0.4.38-in-0.4.40 trap --aborting before anything is signed or packaged."
}
$publishedExeSha = (Get-FileHash -Algorithm SHA256 $publishedExe).Hash
Write-Host "    $exeName FileVersion=$builtFileVersion  sha256=$publishedExeSha" -ForegroundColor Green

# Same guard for the health exe — never ship a stale/mismatched second binary.
$healthFileVersion = (Get-Item $healthPublishedExe).VersionInfo.FileVersion
$healthV3 = (($healthFileVersion -split '\.')[0..2]) -join '.'
if ($healthV3 -ne $V3) {
    throw "HEALTH BINARY VERSION MISMATCH: $healthExeName FileVersion is '$healthFileVersion' (=> $healthV3) but release is $V3."
}
$healthExeSha = (Get-FileHash -Algorithm SHA256 $healthPublishedExe).Hash
Write-Host "    $healthExeName FileVersion=$healthFileVersion  sha256=$healthExeSha" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 4. Sign the exe (on the token) BEFORE packaging, so the MSI captures signed bytes.
# ---------------------------------------------------------------------------
if ($SkipSigning) {
    Write-Host "==> [4/8] Signing exes: SKIPPED (-SkipSigning)" -ForegroundColor Yellow
} else {
    Write-Host "==> [4/8] Signing $exeName + $healthExeName (SafeNet PIN dialog pops)" -ForegroundColor Cyan
    # sign-msix.ps1 runs ErrorActionPreference=Stop and THROWS on any failure, which
    # propagates here. Do NOT test $LASTEXITCODE -- it reflects the last external exe
    # (signtool) inside the child, not the script outcome, and could false-positive.
    try { & (Join-Path $PSScriptRoot "sign-msix.ps1") -Path $publishedExe }
    catch { throw "exe signing failed: $($_.Exception.Message)" }
    try { & (Join-Path $PSScriptRoot "sign-msix.ps1") -Path $healthPublishedExe }
    catch { throw "health exe signing failed: $($_.Exception.Message)" }
}

# Signing rewrites the exe IN PLACE (Authenticode embeds the signature), so the bytes now
# differ from the pre-sign compile hash captured in step 3. WiX packages THIS signed exe,
# so re-hash here and use the signed hash for the step-7 inner-exe comparison and the manifest.
# Without this, the first real signed build aborts in step 7: it compared the signed inner exe
# against the pre-sign hash, a check that can never pass once signing is on -- and -SkipSigning
# dry-runs never exercised it (FIX-RELEASE-SIGNED-HASH-001, 2026-06-07). In -SkipSigning the
# file is unchanged, so this re-hash equals the step-3 value and the check still holds.
$publishedExeSha = (Get-FileHash -Algorithm SHA256 $publishedExe).Hash
$healthExeSha    = (Get-FileHash -Algorithm SHA256 $healthPublishedExe).Hash
if (-not $SkipSigning) {
    Write-Host "    Signed $exeName sha256=$publishedExeSha (re-hashed post-sign)" -ForegroundColor Green
    Write-Host "    Signed $healthExeName sha256=$healthExeSha (re-hashed post-sign)" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 5. WiX build directly against the (now-signed) publish dir. NEVER re-publishes.
# ---------------------------------------------------------------------------
Write-Host "==> [5/8] Building MSI ($V4) from the published dir (no re-publish)" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installerOut | Out-Null
if (Test-Path $msiPath) { Remove-Item $msiPath -Force }

$wix = Get-Command wix.exe -ErrorAction SilentlyContinue
if (-not $wix) { $wix = Get-Item "$env:USERPROFILE\.dotnet\tools\wix.exe" -ErrorAction Stop }

& $wix.Source build $installerSrc `
    -arch x64 `
    -ext WixToolset.UI.wixext `
    -d "PublishDir=$publishDir" `
    -d "HealthPublishDir=$healthPublishDir" `
    -d "ProductVersion=$V4" `
    -d "LogonTaskXmlPath=$logonTaskXml" `
    -d "UpdaterTaskXmlPath=$updaterTaskXml" `
    -d "LicenseRtf=$licenseRtf" `
    -d "AgentSrcDir=$agentSrcDir" `
    -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "wix build failed (exit $LASTEXITCODE)" }
if (-not (Test-Path $msiPath)) { throw "MSI not produced: $msiPath" }

# ---------------------------------------------------------------------------
# 6. Sign the MSI.
# ---------------------------------------------------------------------------
if ($SkipSigning) {
    Write-Host "==> [6/8] Signing MSI: SKIPPED (-SkipSigning)" -ForegroundColor Yellow
} else {
    Write-Host "==> [6/8] Signing MSI (SafeNet PIN dialog may pop; token caches the unlock)" -ForegroundColor Cyan
    # Real-time AV (Windows Defender) scans the freshly-built ~99MB MSI and can hold a
    # transient lock when signtool opens it ("file is being used by another process"),
    # failing the sign. Retry with a short backoff so a scan window can't kill a build
    # the token already unlocked. The token caches the PIN, so retries don't re-prompt.
    $msiSigned = $false
    for ($attempt = 1; $attempt -le 4 -and -not $msiSigned; $attempt++) {
        try { & (Join-Path $PSScriptRoot "sign-msix.ps1") -Path $msiPath; $msiSigned = $true }
        catch {
            $sm = $_.Exception.Message
            $transient = $sm -match 'being used by another process|cannot access the file|Access is denied|denied'
            if ($attempt -lt 4 -and $transient) {
                Write-Host "    MSI sign attempt $attempt hit a transient lock (AV scan?); retrying in 5s..." -ForegroundColor Yellow
                Start-Sleep -Seconds 5
            } else {
                throw "MSI signing failed after $attempt attempt(s): $sm"
            }
        }
    }
}

# ---------------------------------------------------------------------------
# 7. VERIFY the produced package against the compiled binary and the version.
#    Gold standard: prove the binary INSIDE the MSI, not the loose publish dir.
# ---------------------------------------------------------------------------
Write-Host "==> [7/8] Verifying package (extract + version + hash + signature)" -ForegroundColor Cyan

# 7a. MSI ProductVersion from the Property table (no msiexec launch). Use direct
# COM dispatch: PowerShell's reflection InvokeMember binder rejects the optional
# Execute(Record) argument with DISP_E_TYPEMISMATCH, while native dispatch handles
# the COM IDispatch call cleanly.
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null; $view = $null; $record = $null
try {
    $database = $installer.OpenDatabase($msiPath, 0)
    $view = $database.OpenView("SELECT Value FROM Property WHERE Property = 'ProductVersion'")
    $view.Execute()
    $record = $view.Fetch()
    if ($null -eq $record) { throw "MSI has no ProductVersion row in the Property table." }
    $msiProductVersion = $record.StringData(1)
} finally {
    # Release RCWs in reverse acquisition order so the MSI file handle held by
    # OpenDatabase drops deterministically before any later Remove-Item on the MSI.
    foreach ($com in @($record, $view, $database, $installer)) {
        if ($com) { [System.Runtime.InteropServices.Marshal]::ReleaseComObject($com) | Out-Null }
    }
}
if ($msiProductVersion -ne $V4) { throw "MSI ProductVersion is '$msiProductVersion', expected '$V4'." }
Write-Host "    MSI ProductVersion = $msiProductVersion" -ForegroundColor Green

# 7b. Administrative-install extraction -> compare inner exe to the compiled exe.
$extractDir = Join-Path $installerOut "_verify-extract-$V4"
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
try {
    $log = Join-Path $extractDir "admin-install.log"
    # No -Wait: a held global _MSIExecute mutex can block msiexec indefinitely, and
    # -Wait has no timeout. Watchdog with WaitForExit(ms) and kill on hang.
    $proc = Start-Process msiexec.exe -ArgumentList @("/a", "`"$msiPath`"", "/qn", "TARGETDIR=`"$extractDir`"", "/l*v", "`"$log`"") -PassThru
    if (-not $proc.WaitForExit(120000)) {
        try { $proc.Kill() } catch { }
        throw "msiexec /a extraction did not exit within 120s (a held _MSIExecute mutex can block it); see $log"
    }
    if ($proc.ExitCode -ne 0) { throw "msiexec /a extraction failed (exit $($proc.ExitCode)); see $log" }

    $innerExe = Get-ChildItem $extractDir -Recurse -Filter $exeName -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $innerExe) { throw "Could not find $exeName inside the extracted MSI under $extractDir" }

    $innerFileVersion = $innerExe.VersionInfo.FileVersion
    $innerV3 = (($innerFileVersion -split '\.')[0..2]) -join '.'
    $innerSha = (Get-FileHash -Algorithm SHA256 $innerExe.FullName).Hash

    if ($innerV3 -ne $V3) { throw "Inner exe FileVersion '$innerFileVersion' (=> $innerV3) != $V3." }
    if ($innerSha -ne $publishedExeSha) {
        throw "Inner exe is NOT byte-identical to the packaged exe.`n  packaged: $publishedExeSha`n  in MSI  : $innerSha`nThe MSI is carrying a different binary than was built+signed+verified --aborting."
    }
    Write-Host "    Inner exe FileVersion=$innerFileVersion  sha256=$innerSha  (byte-identical to packaged exe)" -ForegroundColor Green

    # Same byte-identity proof for the health service exe inside the MSI.
    $innerHealthExe = Get-ChildItem $extractDir -Recurse -Filter $healthExeName -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $innerHealthExe) { throw "Could not find $healthExeName inside the extracted MSI under $extractDir" }
    $innerHealthFileVersion = $innerHealthExe.VersionInfo.FileVersion
    $innerHealthV3 = (($innerHealthFileVersion -split '\.')[0..2]) -join '.'
    $innerHealthSha = (Get-FileHash -Algorithm SHA256 $innerHealthExe.FullName).Hash
    if ($innerHealthV3 -ne $V3) { throw "Inner health exe FileVersion '$innerHealthFileVersion' (=> $innerHealthV3) != $V3." }
    if ($innerHealthSha -ne $healthExeSha) {
        throw "Inner health exe is NOT byte-identical to the packaged exe.`n  packaged: $healthExeSha`n  in MSI  : $innerHealthSha"
    }
    Write-Host "    Inner $healthExeName FileVersion=$innerHealthFileVersion  sha256=$innerHealthSha  (byte-identical)" -ForegroundColor Green

    # 7c. Authenticode (only when we actually signed).
    if (-not $SkipSigning) {
        foreach ($target in @(@{ Name = "MSI"; Path = $msiPath }, @{ Name = "inner exe"; Path = $innerExe.FullName }, @{ Name = "inner health exe"; Path = $innerHealthExe.FullName })) {
            $sig = Get-AuthenticodeSignature -LiteralPath $target.Path
            if ($sig.Status -ne "Valid") { throw "$($target.Name) Authenticode is '$($sig.Status)', expected Valid." }
            if ($sig.SignerCertificate.Thumbprint -ne $expectedThumbprint) {
                throw "$($target.Name) signed with unexpected cert $($sig.SignerCertificate.Thumbprint) (expected $expectedThumbprint)."
            }
        }
        Write-Host "    Authenticode Valid on MSI + inner exe + inner health exe; cert thumbprint $expectedThumbprint" -ForegroundColor Green
    }
} finally {
    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue }
}

$msiItem    = Get-Item $msiPath
$msiSha      = (Get-FileHash -Algorithm SHA256 $msiPath).Hash
$signerSubject = $null
if (-not $SkipSigning) { $signerSubject = (Get-AuthenticodeSignature -LiteralPath $msiPath).SignerCertificate.Subject }

# Optional Intune wrap (signed MSI only).
$intunewinPath = $null
if ($Intune) {
    Write-Host "    Wrapping signed MSI into .intunewin" -ForegroundColor Cyan
    try { & (Join-Path $PSScriptRoot "build-intunewin.ps1") -Version $V4 }
    catch { throw "build-intunewin.ps1 failed: $($_.Exception.Message)" }
    $intunewinPath = Join-Path $repoRoot "artifacts\intune-out\ToastNotification.Agent-$V4.intunewin"
}

# ---------------------------------------------------------------------------
# 8. Emit the machine-readable release manifest.
# ---------------------------------------------------------------------------
Write-Host "==> [8/8] Writing release manifest" -ForegroundColor Cyan
$gitCommit = (& git -C $repoRoot rev-parse HEAD 2>$null)
if (-not $gitCommit) { $gitCommit = "unknown" }

$manifestObj = [ordered]@{
    product           = "Toast Notification Agent"
    version           = $V3
    msiProductVersion = $V4
    runtime           = $RuntimeIdentifier
    buildUtc          = (Get-Date).ToUniversalTime().ToString("o")
    gitCommit         = $gitCommit.Trim()
    signed            = (-not $SkipSigning)
    signerThumbprint  = $(if ($SkipSigning) { $null } else { $expectedThumbprint })
    signerSubject     = $signerSubject
    msi = [ordered]@{
        fileName  = $msiItem.Name
        sizeBytes = $msiItem.Length
        sha256    = $msiSha
    }
    exe = [ordered]@{
        fileName    = $exeName
        fileVersion = $builtFileVersion
        sha256      = $publishedExeSha
    }
    healthExe = [ordered]@{
        fileName    = $healthExeName
        fileVersion = $healthFileVersion
        sha256      = $healthExeSha
    }
    intunewin = $(if ($intunewinPath) { @{ fileName = (Split-Path $intunewinPath -Leaf); sha256 = (Get-FileHash -Algorithm SHA256 $intunewinPath).Hash } } else { $null })
    downloadUrls = [ordered]@{
        msi        = "https://toastnotification.com/downloads/ToastNotification.msi"
        versionApi = "https://toastnotification.com/api/agent/version"
    }
}
Write-Utf8 $manifestOut (($manifestObj | ConvertTo-Json -Depth 6))

Write-Host ""
Write-Host "================ RELEASE BUILD COMPLETE =====================" -ForegroundColor Green
Write-Host "  MSI      : $($msiItem.FullName)" -ForegroundColor Green
Write-Host ("  Size     : {0:N2} MB" -f ($msiItem.Length / 1MB))
Write-Host "  Signed   : $(-not $SkipSigning)"
Write-Host "  Manifest : $manifestOut" -ForegroundColor Green
if ($SkipSigning) {
    Write-Host ""
    Write-Host "  *** UNSIGNED dry-run. NOT for distribution --install scripts reject it. ***" -ForegroundColor Yellow
}
Write-Host "============================================================" -ForegroundColor Green

# Version-bump commit hygiene: stamping may have dirtied the source. The version
# bump must land in the SAME commit as the code change --never auto-commit here.
$dirty = (& git -C $repoRoot status --short -- `
    "src/ToastRevival.Agent/ToastRevival.Agent.csproj" `
    "src/ToastRevival.Api/appsettings.json" `
    "src/ToastRevival.Agent/Package.appxmanifest" 2>$null)
if ($dirty) {
    Write-Host ""
    Write-Host "NOTE: version stamping changed source files. Commit the bump WITH the code change:" -ForegroundColor Yellow
    $dirty | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "NEXT (deploy --see Docs/ToastRevival/DEPLOY-PATHS.md):"
Write-Host "  1. Commit the version bump + code change; push."
Write-Host "  2. scp the signed MSI to TOASTWEB1; sudo cp to /opt/toast/downloads/ToastNotification.msi"
Write-Host "     AND /opt/toast/downloads/ToastNotification.Agent-$V4.msi ; sudo chown toast:toast."
Write-Host "  3. Set Agent__LatestVersion=$V3 in /opt/toast/.env ; sudo systemctl restart toast-api."
Write-Host "  4. Verify: curl https://toastnotification.com/api/agent/version  (=> $V3)"
Write-Host "     and the served-MSI sha256 == manifest sha256 ($msiSha)."
Write-Host "  5. REFRESH THE .intunewin (admin panel 'Download .intunewin' serves it -- stale = old agent):"
if ($Intune) {
    Write-Host "     done by -Intune -> artifacts/intune-out/. scp BOTH ToastNotification.intunewin (canonical)"
    Write-Host "     + ToastNotification.Agent-$V4.intunewin to /opt/toast/downloads/ ; sudo chown toast:toast."
} else {
    Write-Host "     scripts/build-intunewin.ps1 -Version $V4 ; scp ToastNotification.intunewin +"
    Write-Host "     ToastNotification.Agent-$V4.intunewin to /opt/toast/downloads/ ; sudo chown toast:toast."
}
Write-Host "     Verify: curl https://toastnotification.com/api/agent/intunewin-info (=> version $V3, fresh lastModified)."
