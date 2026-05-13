<#
.SYNOPSIS
    Sync a release tag from the private working repo to the public mirror at
    https://github.com/keithrlucier/toast-notification.

.DESCRIPTION
    Workflow contract is documented in Docs/PUBLIC-MIRROR.md. Summary:

      1. Verify the release tag exists locally in the private repo.
      2. Check out (or clone) the public mirror as a sibling worktree.
      3. Wipe everything in the public worktree except .git/.
      4. Walk the private working tree and copy every file the .publicignore
         manifest does NOT exclude (and that .gitignore does not exclude).
      5. Run literal-string substitutions over every text file to sanitize
         production IPs, SSH key paths, and internal hostnames.
      6. Commit with "Public mirror release <tag>" + the private tag's
         annotation as the message body.
      7. Tag the public commit with the same release tag.
      8. Push branch and tag to origin (the public remote).

    The script is idempotent — re-running with the same tag is a no-op if the
    public side already carries that tag, unless -Force is passed.

.PARAMETER Tag
    Release tag to mirror. Must exist locally as a git tag. Convention is
    SemVer (e.g. v0.4.8).

.PARAMETER PublicRepoUrl
    The public mirror remote. Defaults to the canonical URL from
    Docs/PUBLIC-MIRROR.md.

.PARAMETER WorktreePath
    Local path where the public mirror lives (cloned on first run, updated
    on subsequent runs). Defaults to ..\toast-public-mirror sibling to this
    repo.

.PARAMETER Pat
    GitHub PAT with repo scope on keithrlucier/toast-notification. Read from
    $env:TOAST_PUBLIC_PAT when omitted. Required for push.

.PARAMETER DryRun
    Run every step except commit + push. Prints the public-side git status
    and a diff stat so the operator can audit what would ship.

.PARAMETER Force
    Allow retagging an already-mirrored tag. Use only when re-syncing after
    a sanitization rule fix; otherwise prefer a new patch version.

.EXAMPLE
    .\scripts\sync-public-mirror.ps1 -Tag v0.4.8 -DryRun

.EXAMPLE
    $env:TOAST_PUBLIC_PAT = "github_pat_..."
    .\scripts\sync-public-mirror.ps1 -Tag v0.4.8
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Tag,
    [string] $PublicRepoUrl = "https://github.com/keithrlucier/toast-notification.git",
    [string] $WorktreePath = "..\toast-public-mirror",
    [string] $Pat = $env:TOAST_PUBLIC_PAT,
    [switch] $DryRun,
    [switch] $Force
)

$ErrorActionPreference = "Stop"

# === Paths ===
$privateRoot = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$publicRoot  = if ([System.IO.Path]::IsPathRooted($WorktreePath)) {
    $WorktreePath
} else {
    [System.IO.Path]::GetFullPath((Join-Path $privateRoot $WorktreePath))
}
$publicIgnore = Join-Path $privateRoot ".publicignore"

Write-Host "==> Private repo:  $privateRoot"
Write-Host "==> Public mirror: $publicRoot"
Write-Host "==> Release tag:   $Tag"
Write-Host ""

# === 1. Verify tag exists locally ===
git -C $privateRoot rev-parse "refs/tags/$Tag" 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Tag '$Tag' does not exist in the private repo. Cut the tag first: git tag -a $Tag -m '...' && git push origin $Tag"
}
$tagSha     = (git -C $privateRoot rev-list -n 1 "$Tag").Trim()
$tagMessage = (git -C $privateRoot for-each-ref --format='%(contents)' "refs/tags/$Tag").Trim()
Write-Host "==> Tag $Tag -> $tagSha"

# === 2. Clone or update the public mirror worktree ===
if (-not (Test-Path $publicRoot)) {
    Write-Host "==> Cloning public mirror to $publicRoot"
    git clone $PublicRepoUrl $publicRoot
    if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
} else {
    Write-Host "==> Updating existing public mirror worktree"
    git -C $publicRoot fetch origin
    git -C $publicRoot checkout main
    git -C $publicRoot reset --hard origin/main
}

# Bail early if this tag is already on the public side and we're not forcing.
$publicTag = (git -C $publicRoot tag --list $Tag).Trim()
if ($publicTag -eq $Tag -and -not $Force) {
    Write-Host "==> Tag $Tag already exists on the public mirror. Pass -Force to retag." -ForegroundColor Yellow
    exit 0
}

# === 3. Wipe the public worktree except .git/ ===
Write-Host "==> Clearing public worktree (preserving .git/)"
Get-ChildItem -LiteralPath $publicRoot -Force | Where-Object { $_.Name -ne ".git" } | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}

# === 4. Copy every file the private repo has that .gitignore + .publicignore allow ===
# Use `git ls-files` to get the set of tracked files (this already honors
# .gitignore). Then filter through .publicignore patterns.
Write-Host "==> Enumerating private repo tracked files"
$privateTrackedRaw = git -C $privateRoot ls-files
if ($LASTEXITCODE -ne 0) { throw "git ls-files failed in private repo" }
$privateTracked = $privateTrackedRaw -split "`r?`n" | Where-Object { $_ }

# Parse .publicignore into exclude/include patterns.
$excludeGlobs = New-Object System.Collections.Generic.List[string]
$includeGlobs = New-Object System.Collections.Generic.List[string]
if (Test-Path $publicIgnore) {
    foreach ($line in Get-Content $publicIgnore) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#")) { continue }
        if ($trimmed.StartsWith("!")) {
            $includeGlobs.Add($trimmed.Substring(1).TrimStart('/'))
        } else {
            $excludeGlobs.Add($trimmed.TrimStart('/'))
        }
    }
}

function Test-MatchGlob {
    param([string]$Path, [string]$Pattern)
    # Normalize to forward slashes for matching.
    $p = $Path -replace '\\', '/'
    $pat = $Pattern -replace '\\', '/'

    # Trailing slash means "directory and everything under it".
    if ($pat.EndsWith('/')) {
        $prefix = $pat
        return $p.StartsWith($prefix) -or $p -eq $prefix.TrimEnd('/')
    }

    # Convert glob to regex: ** -> .*, * -> [^/]*, ? -> [^/], escape the rest.
    $rx = [regex]::Escape($pat)
    $rx = $rx -replace '\\\*\\\*', '.*'
    $rx = $rx -replace '\\\*', '[^/]*'
    $rx = $rx -replace '\\\?', '[^/]'
    return $p -match "^$rx$"
}

function Test-IsPublic {
    param([string]$Path)
    # Walk: every file is public unless an exclude matches; an include reinstates it.
    $excluded = $false
    foreach ($g in $excludeGlobs) {
        if (Test-MatchGlob -Path $Path -Pattern $g) { $excluded = $true; break }
    }
    if (-not $excluded) { return $true }
    foreach ($g in $includeGlobs) {
        if (Test-MatchGlob -Path $Path -Pattern $g) { return $true }
    }
    return $false
}

# Switch the private repo to the requested tag for the file enumeration.
$privateOriginalRef = (git -C $privateRoot rev-parse --abbrev-ref HEAD).Trim()
$privateOriginalSha = (git -C $privateRoot rev-parse HEAD).Trim()
try {
    Write-Host "==> Checking out $Tag in private repo"
    git -C $privateRoot checkout --quiet $Tag
    if ($LASTEXITCODE -ne 0) { throw "git checkout $Tag failed in private repo" }

    $copiedCount  = 0
    $skippedCount = 0
    foreach ($rel in $privateTracked) {
        if (-not $rel) { continue }
        if (-not (Test-IsPublic -Path $rel)) {
            $skippedCount++
            continue
        }
        $src = Join-Path $privateRoot $rel
        if (-not (Test-Path $src)) { continue }
        $dst = Join-Path $publicRoot $rel
        $dstDir = Split-Path -Parent $dst
        if ($dstDir -and -not (Test-Path $dstDir)) {
            New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
        }
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $copiedCount++
    }

    Write-Host "==> Copied $copiedCount files, skipped $skippedCount (private-only)"

    # === 5. Sanitize literal strings in every text file ===
    $substitutions = @{
        '54.82.103.160'                                = '<your-web-server-ip>'
        '172.26.0.161'                                 = '<your-web-private-ip>'
        '172.26.3.164'                                 = '<your-db-private-ip>'
        '52.21.249.120'                                = '<your-build-server-ip>'
        '34.194.10.242'                                = '<your-paradise-server-ip>'
        'Toast_Web_LightsailDefaultKey-us-east-1.pem'  = '<your-ssh-key.pem>'
        'Toast_Data_1_LightsailDefaultKey-us-east-1.pem' = '<your-db-ssh-key.pem>'
    }

    # Walk every file we just copied. Match on extension to skip binaries.
    $textExtensions = @('.cs','.ts','.tsx','.js','.jsx','.json','.yml','.yaml','.md','.txt','.ps1','.psm1','.psd1','.sh','.html','.css','.scss','.svg','.xml','.config','.csproj','.sln','.editorconfig','.gitignore','.gitattributes','.dockerfile','.dockerignore','.toml','.ini','.cfg','.conf','.wxs','.wxl','.appxmanifest','.template','.sample','.example')
    $sanitizedFiles = 0
    Get-ChildItem -LiteralPath $publicRoot -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\\.git\\'
    } | ForEach-Object {
        $ext = $_.Extension.ToLowerInvariant()
        if (-not ($textExtensions -contains $ext) -and $_.Name -ne 'Dockerfile' -and $_.Name -ne 'LICENSE') {
            return
        }
        $content = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $content) { return }
        $changed = $false
        foreach ($k in $substitutions.Keys) {
            if ($content.Contains($k)) {
                $content = $content.Replace($k, $substitutions[$k])
                $changed = $true
            }
        }
        if ($changed) {
            Set-Content -LiteralPath $_.FullName -Value $content -NoNewline -Encoding utf8
            $sanitizedFiles++
        }
    }
    Write-Host "==> Sanitized literal strings in $sanitizedFiles files"

    # === DryRun exit point ===
    if ($DryRun) {
        Write-Host ""
        Write-Host "==> DRY RUN — public worktree at $publicRoot" -ForegroundColor Yellow
        Write-Host ""
        git -C $publicRoot add --all
        git -C $publicRoot status --short
        Write-Host ""
        git -C $publicRoot diff --cached --stat
        Write-Host ""
        Write-Host "==> Run the audit checklist in Docs/PUBLIC-MIRROR.md against $publicRoot" -ForegroundColor Yellow
        Write-Host "==> Re-run without -DryRun to commit + push." -ForegroundColor Yellow
        return
    }

    # === 6. Commit ===
    if (-not $Pat) {
        throw "No PAT provided. Pass -Pat or set TOAST_PUBLIC_PAT in env."
    }

    Write-Host "==> Staging + committing public mirror"
    git -C $publicRoot add --all

    # If nothing changed (re-run of an already-mirrored tag), bail clean.
    $status = git -C $publicRoot status --porcelain
    if (-not $status) {
        Write-Host "==> Public worktree already matches private $Tag. Nothing to commit." -ForegroundColor Yellow
        if ($Force) {
            Write-Host "==> -Force passed; retagging only" -ForegroundColor Yellow
            git -C $publicRoot tag -d $Tag 2>$null
            git -C $publicRoot tag -a $Tag -m "Public mirror release $Tag"
            $pushUrl = $PublicRepoUrl -replace 'https://', "https://x-access-token:$Pat@"
            git -C $publicRoot push $pushUrl --tags --force
        }
        return
    }

    $commitMessage = "Public mirror release $Tag`n`n$tagMessage"
    $commitMessage | git -C $publicRoot commit -F -
    if ($LASTEXITCODE -ne 0) { throw "git commit failed on public mirror" }

    # === 7. Tag ===
    if ($Force) { git -C $publicRoot tag -d $Tag 2>$null }
    git -C $publicRoot tag -a $Tag -m "Public mirror release $Tag"

    # === 8. Push (URL-embedded PAT, never written to disk) ===
    Write-Host "==> Pushing main + $Tag to $PublicRepoUrl"
    $pushUrl = $PublicRepoUrl -replace 'https://', "https://x-access-token:$Pat@"
    git -C $publicRoot push $pushUrl main
    if ($LASTEXITCODE -ne 0) { throw "git push (branch) failed" }
    git -C $publicRoot push $pushUrl --tags
    if ($LASTEXITCODE -ne 0) { throw "git push (tags) failed" }

    Write-Host ""
    Write-Host "==> Public mirror release $Tag complete." -ForegroundColor Green
    Write-Host "    $($PublicRepoUrl -replace '\.git$','')/releases/tag/$Tag"
}
finally {
    # Restore the private repo to whatever branch/ref it was on before.
    if ($privateOriginalRef -ne 'HEAD') {
        git -C $privateRoot checkout --quiet $privateOriginalRef
    } else {
        git -C $privateRoot checkout --quiet $privateOriginalSha
    }
}
