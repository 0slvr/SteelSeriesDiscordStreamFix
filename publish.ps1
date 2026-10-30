<#
    publish.ps1  (split-repo version)
    ---------------------------------
    Two GitHub repos:
      * MAIN repo    (users see it): each release contains ONLY Setup.exe
      * UPDATES repo (hidden)      : holds the Velopack update files
                                     (full.nupkg, delta.nupkg, releases.win.json ...)
                                     and is the repo AppUpdater.cs reads from.

    Usage:
        .\publish.ps1 -Transition      # ONE-TIME: full release to BOTH repos (so old installs can migrate)
        .\publish.ps1                  # normal release (after the transition)
        .\publish.ps1 -Notes "Fixed mic mute bug"
        .\publish.ps1 -Bump minor      # 1.0.20 -> 1.1.0
        .\publish.ps1 -VersionOverride 2.0.0
        .\publish.ps1 -DryRun          # build + pack only, nothing pushed or uploaded
        .\publish.ps1 -SkipGit         # do not commit/push source code
        .\publish.ps1 -UploadOnly -VersionOverride 1.0.21   # retry uploads (add -Transition if it was a transition run)

    Requirements:
      - dotnet SDK, git, and:  dotnet tool install -g vpk
      - Environment variable GH_TOKEN with "Contents: Read and write" on BOTH repos
        setx GH_TOKEN "ghp_xxx"   (then restart Visual Studio)
      - The updates repo must be Public and have at least one commit (e.g. a README).
#>

[CmdletBinding()]
param(
    [string]$VersionOverride,
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch",
    [string]$Notes,
    [switch]$Transition,
    [switch]$SkipGit,
    [switch]$UploadOnly,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---------------- Configuration ----------------
$repoUrl        = "https://github.com/0slvr/SteelSeriesDiscordStreamFix"           # main repo (users download Setup.exe here)
$updatesRepoUrl = "https://github.com/0slvr/SteelSeriesDiscordStreamFix-updates"   # hidden repo for update files
$projectFile    = "SteelSeriesDiscordStreamFix.csproj"
$exeName        = "SteelSeriesDiscordStreamFix.exe"
$appId          = "SteelSeriesDiscordStreamFix"
$iconFile       = "steelseries.ico"
$buildPortable  = $false        # $false = skip Portable.zip
$updateAssemblyInfo = $true     # also write the version into Properties\AssemblyInfo.cs if it exists
# -----------------------------------------------

$root        = $PSScriptRoot
$versionFile = Join-Path $root "version.txt"
$publishDir  = Join-Path $root "publish"
$releasesDir = Join-Path $root "Releases"
$logsDir     = Join-Path $root "logs"
$iconPath    = Join-Path $root $iconFile
$asmInfoPath = Join-Path $root "Properties\AssemblyInfo.cs"
$updaterPath = Join-Path $root "AppUpdater.cs"
$setupName   = "$appId-win-Setup.exe"
$notesFile   = Join-Path $env:TEMP "release-notes-$appId.md"

# ---------------- Helpers ----------------
function Step([string]$m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Info([string]$m) { Write-Host "    $m" -ForegroundColor Gray }
function Warn([string]$m) { Write-Host "    [!] $m" -ForegroundColor Yellow }

function Run {
    param([string]$Exe, [string[]]$Arguments, [switch]$AllowFail)
    & $Exe @Arguments | Out-Host
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not $AllowFail) {
        throw "'$Exe $($Arguments[0])' failed (exit code $code)."
    }
    return $code
}

function GitOut {
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { $o = & git -C $root @args 2>$null; return $o }
    finally { $ErrorActionPreference = $old }
}

function Require-Command([string]$name, [string]$hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { throw "'$name' was not found. $hint" }
}

function Get-RepoParts([string]$url) {
    if ($url -match 'github\.com/([^/]+)/([^/]+?)(?:\.git)?/?$') {
        return @{ Owner = $Matches[1]; Name = $Matches[2] }
    }
    throw "Not a valid GitHub repo URL: $url"
}

function Get-GhHeaders {
    return @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = "application/vnd.github+json"; "User-Agent" = "publish-ps1" }
}

function Test-ReleaseTag([string]$url, [string]$tag) {
    $p = Get-RepoParts $url
    try {
        Invoke-RestMethod -Uri "https://api.github.com/repos/$($p.Owner)/$($p.Name)/releases/tags/$tag" -Headers (Get-GhHeaders) | Out-Null
        return $true
    }
    catch { return $false }
}

function Test-HasReleases([string]$url) {
    $p = Get-RepoParts $url
    try {
        $r = Invoke-RestMethod -Uri "https://api.github.com/repos/$($p.Owner)/$($p.Name)/releases?per_page=1" -Headers (Get-GhHeaders)
        return (@($r).Count -gt 0)
    }
    catch { return $false }
}

# Upload the Velopack update files (full/delta/json/Setup) with vpk.
function Upload-Vpk([string]$url) {
    Run vpk @("upload", "github", "--repoUrl", $url, "--token", $env:GH_TOKEN,
              "--publish", "--pre", "--tag", "v$version", "--releaseName", "v$version",
              "-o", $releasesDir) | Out-Null
}

# Create a release that contains ONLY Setup.exe (draft -> upload -> publish, so no half-made "Latest").
function Publish-SetupOnlyRelease([string]$url, [string]$branchName, [string]$setupPath, [string]$body) {
    $p = Get-RepoParts $url
    $h = Get-GhHeaders
    $api = "https://api.github.com/repos/$($p.Owner)/$($p.Name)"

    $payload = @{ tag_name = "v$version"; name = "v$version"; body = $body; draft = $true; prerelease = $true }
    if ($branchName) { $payload.target_commitish = $branchName }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json))
    $rel = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $h -Body $bytes -ContentType "application/json; charset=utf-8"

    $upload = "https://uploads.github.com/repos/$($p.Owner)/$($p.Name)/releases/$($rel.id)/assets?name=$([uri]::EscapeDataString($setupName))"
    Invoke-RestMethod -Method Post -Uri $upload -Headers $h -InFile $setupPath -ContentType "application/octet-stream" | Out-Null

    $pub = [Text.Encoding]::UTF8.GetBytes((@{ draft = $false; make_latest = "false" } | ConvertTo-Json))
    Invoke-RestMethod -Method Patch -Uri "$api/releases/$($rel.id)" -Headers $h -Body $pub -ContentType "application/json; charset=utf-8" | Out-Null
}

$useGit        = -not $SkipGit
$asmOriginal   = $null
$committed     = $false
$transcriptOn  = $false
$stopwatch     = [System.Diagnostics.Stopwatch]::StartNew()

try {
    if (-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir | Out-Null }
    $logPath = Join-Path $logsDir ("publish-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
    try { Start-Transcript -Path $logPath | Out-Null; $transcriptOn = $true } catch { }

    # ---------------- 0. Preflight ----------------
    Step "Preflight checks"

    Require-Command "dotnet" "Install the .NET SDK."
    Require-Command "vpk"    "Run: dotnet tool install -g vpk"

    if ($useGit -and -not (Get-Command git -ErrorAction SilentlyContinue)) {
        $gitPaths = @(
            "$env:ProgramFiles\Git\cmd\git.exe",
            "${env:ProgramFiles(x86)}\Git\cmd\git.exe",
            "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe",
            "$env:ProgramFiles\Microsoft Visual Studio\*\*\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
        )
        $found = $gitPaths | ForEach-Object { Get-Item -Path $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
        if ($found) {
            $env:PATH = "$($found.DirectoryName);$env:PATH"
            Info "Using git from: $($found.FullName)"
        }
    }
    if ($useGit) { Require-Command "git" "Install Git for Windows (winget install --id Git.Git -e), then restart Visual Studio." }

    if (-not $env:GH_TOKEN) {
        throw "GH_TOKEN is not set. Run: setx GH_TOKEN `"ghp_xxx`"  then restart Visual Studio."
    }
    if (-not (Test-Path (Join-Path $root $projectFile))) { throw "Project file not found: $projectFile" }
    if (-not (Test-Path $iconPath)) { throw "Icon file not found: $iconPath" }

    # AppUpdater.cs must point at the updates repo, otherwise installed apps would never find new versions.
    if (-not $UploadOnly) {
        if (-not (Test-Path $updaterPath)) { throw "AppUpdater.cs not found at $updaterPath" }
        $updaterText = [System.IO.File]::ReadAllText($updaterPath)
        if ($updaterText -notmatch [regex]::Escape($updatesRepoUrl)) {
            throw "AppUpdater.cs does not point to $updatesRepoUrl. Change GithubRepoUrl there first."
        }
    }

    # Normal (non-transition) releases need the updates repo to already contain the transition release.
    if (-not $Transition -and -not $DryRun) {
        if (-not (Test-HasReleases $updatesRepoUrl)) {
            throw "The updates repo has no releases yet. Run the one-time transition first:  .\publish.ps1 -Transition"
        }
    }

    $branch = $null
    if ($useGit) {
        $inside = GitOut rev-parse --is-inside-work-tree
        if ($inside -ne "true") { throw "This folder is not a git repository. Use -SkipGit or run 'git init'." }

        $branch = (GitOut rev-parse --abbrev-ref HEAD | Select-Object -First 1)
        if ($branch -eq "HEAD") { throw "Git is in a detached HEAD state. Checkout a branch first." }
        Info "Git branch: $branch"

        Info "Fetching tags from remote..."
        GitOut fetch --tags --quiet | Out-Null

        $gitignorePath = Join-Path $root ".gitignore"
        $needed = @("publish/", "Releases/", "logs/", "bin/", "obj/", ".vs/")
        $existing = @()
        if (Test-Path $gitignorePath) { $existing = Get-Content $gitignorePath }
        $missing = $needed | Where-Object { $existing -notcontains $_ -and $existing -notcontains "/$_" }
        if ($missing) {
            Add-Content -Path $gitignorePath -Value (@("", "# added by publish.ps1") + $missing)
            Info ("Added to .gitignore: " + ($missing -join ", "))
        }
    }

    # ---------------- 1. Version ----------------
    Step "Choosing version"

    $candidates = @()
    if (Test-Path $versionFile) {
        $t = (Get-Content $versionFile -Raw).Trim()
        if ($t -match '^\d+\.\d+\.\d+$') { $candidates += [version]$t }
    }
    if ($useGit) {
        foreach ($tag in (GitOut tag -l "v*")) {
            if ($tag -match '^v(\d+\.\d+\.\d+)$') { $candidates += [version]$Matches[1] }
        }
    }
    if ($candidates.Count -gt 0) { $latest = $candidates | Sort-Object -Descending | Select-Object -First 1 }
    else { $latest = [version]"1.0.0" }

    if ($VersionOverride) {
        if ($VersionOverride -notmatch '^\d+\.\d+\.\d+$') { throw "VersionOverride must look like 1.2.3" }
        $version = $VersionOverride
    }
    elseif ($UploadOnly) {
        $version = $latest.ToString()
    }
    else {
        switch ($Bump) {
            "major" { $version = "{0}.0.0" -f ($latest.Major + 1) }
            "minor" { $version = "{0}.{1}.0" -f $latest.Major, ($latest.Minor + 1) }
            default { $version = "{0}.{1}.{2}" -f $latest.Major, $latest.Minor, ($latest.Build + 1) }
        }
    }

    if (-not $UploadOnly -and $useGit) {
        $remoteTag = GitOut ls-remote --tags origin "refs/tags/v$version"
        if ($remoteTag) { throw "Tag v$version already exists on GitHub. Use a higher -VersionOverride." }
    }

    $modeName = if ($Transition) { "TRANSITION (both repos)" } else { "normal (updates repo + Setup-only release)" }
    Write-Host "==> Publishing version $version (previous: $latest) - mode: $modeName" -ForegroundColor Green
    if ($DryRun) { Warn "DRY RUN: nothing will be pushed or uploaded." }

    # ---------------- Release notes ----------------
    $notesText = $Notes
    if (-not $notesText -and $useGit) {
        $lastTag = (GitOut describe --tags --abbrev=0 | Select-Object -First 1)
        if ($lastTag) { $lines = GitOut log "$lastTag..HEAD" --pretty="- %s" }
        else { $lines = GitOut log -20 --pretty="- %s" }
        if ($lines) { $notesText = ($lines -join "`n") }
    }
    if (-not $notesText) { $notesText = "Release v$version" }
    Set-Content -Path $notesFile -Value $notesText -Encoding UTF8

    if (-not $UploadOnly) {

        # ---------------- Assembly version ----------------
        if ($updateAssemblyInfo -and (Test-Path $asmInfoPath)) {
            $asmOriginal = [System.IO.File]::ReadAllText($asmInfoPath)
            $four = "$($version).0"
            $new = $asmOriginal
            $new = [regex]::Replace($new, '(?m)^(\s*\[assembly:\s*AssemblyVersion\(")[^"]*("\)\])', "`${1}$four`${2}")
            $new = [regex]::Replace($new, '(?m)^(\s*\[assembly:\s*AssemblyFileVersion\(")[^"]*("\)\])', "`${1}$four`${2}")
            if ($new -ne $asmOriginal) {
                [System.IO.File]::WriteAllText($asmInfoPath, $new, (New-Object System.Text.UTF8Encoding($true)))
                Info "AssemblyInfo.cs updated to $four"
            }
            else { $asmOriginal = $null }
        }

        # ---------------- 2. Build ----------------
        Step "Building (Release)"
        if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
        Run dotnet @("build", (Join-Path $root $projectFile), "-c", "Release", "-o", $publishDir, "-p:Version=$version", "--nologo") | Out-Null
        if (-not (Test-Path (Join-Path $publishDir $exeName))) { throw "Build finished but '$exeName' is missing in $publishDir" }

        # ---------------- 3. Previous release (for deltas) ----------------
        $downloadRepo = if ($Transition) { $repoUrl } else { $updatesRepoUrl }
        Step "Downloading previous release for delta packages ($downloadRepo)"
        $dl = Run vpk @("download", "github", "--repoUrl", $downloadRepo, "--token", $env:GH_TOKEN, "-o", $releasesDir) -AllowFail
        if ($dl -ne 0) { Warn "Could not download the previous release (first release or offline). Continuing with local files." }

        # ---------------- 4. Pack ----------------
        Step "Packing with vpk"
        $packArgs = @("pack", "-u", $appId, "-v", $version, "-p", $publishDir, "--mainExe", $exeName,
                      "--icon", $iconPath, "-o", $releasesDir, "--releaseNotes", $notesFile)
        if (-not $buildPortable) { $packArgs += "--noPortable" }
        Run vpk $packArgs | Out-Null

        if (-not (Test-Path (Join-Path $releasesDir $setupName))) { throw "$setupName was not created in $releasesDir" }

        # ---------------- 5. Source code ----------------
        if (-not $DryRun) { Set-Content -Path $versionFile -Value $version -NoNewline }

        if ($useGit -and -not $DryRun) {
            Step "Committing and pushing source code"
            $msg = if ($Notes) { ($Notes -split "`r?`n")[0] } else { "Release v$version" }
            Run git @("-C", $root, "add", "-A") | Out-Null
            $pending = GitOut status --porcelain
            if ($pending) {
                Run git @("-C", $root, "commit", "-m", $msg) | Out-Null
                $committed = $true
            }
            else { Info "Nothing to commit." }

            $upstream = GitOut rev-parse --abbrev-ref "@{u}"
            if ($upstream) {
                $behind = GitOut rev-list --count "HEAD..@{u}"
                if ([int]$behind -gt 0) {
                    Info "Remote has $behind newer commit(s); rebasing..."
                    Run git @("-C", $root, "pull", "--rebase") | Out-Null
                }
                Run git @("-C", $root, "push") | Out-Null
            }
            else {
                Run git @("-C", $root, "push", "-u", "origin", $branch) | Out-Null
            }
        }
        elseif ($DryRun) {
            Info "Dry run: skipped version.txt update and git commit/push."
        }
    }

    # ---------------- 6. Upload ----------------
    if ($DryRun) {
        Step "Dry run finished. Packages are in: $releasesDir"
    }
    else {
        $retry = ".\publish.ps1 -UploadOnly -VersionOverride $version"
        if ($Transition) { $retry += " -Transition" }

        try {
            if ($Transition) {
                # Full release to BOTH repos: old installs read the main repo, the new build reads the updates repo.
                foreach ($url in @($repoUrl, $updatesRepoUrl)) {
                    if (Test-ReleaseTag $url "v$version") { Info "v$version already exists in $url, skipping."; continue }
                    Step "Uploading full release to $url"
                    Upload-Vpk $url
                }
            }
            else {
                if (Test-ReleaseTag $updatesRepoUrl "v$version") { Info "v$version already exists in the updates repo, skipping." }
                else {
                    Step "Uploading update files to the updates repo"
                    Upload-Vpk $updatesRepoUrl
                }

                if (Test-ReleaseTag $repoUrl "v$version") { Info "v$version already exists in the main repo, skipping." }
                else {
                    Step "Creating Setup-only release in the main repo"
                    $setupHash = (Get-FileHash -Path (Join-Path $releasesDir $setupName) -Algorithm SHA256).Hash
                    $body = "**Download: $setupName** and run it. Updates install automatically.`n`n$notesText`n`nSHA256 ($setupName): ``$setupHash``"
                    Publish-SetupOnlyRelease $repoUrl $branch (Join-Path $releasesDir $setupName) $body
                }
            }
        }
        catch {
            Warn "Upload failed. Code is already pushed and packages are ready."
            Warn "Retry with:  $retry"
            throw
        }

        Write-Host ""
        Write-Host "==> Done! Version $version is live." -ForegroundColor Green
        Write-Host "    Main repo release : $repoUrl/releases/tag/v$version" -ForegroundColor Green
        Write-Host "    Direct download   : $repoUrl/releases/latest/download/$setupName" -ForegroundColor Green
    }

    $stopwatch.Stop()
    Info ("Total time: {0:N1}s" -f $stopwatch.Elapsed.TotalSeconds)
    $exitCode = 0
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red

    if ($asmOriginal -and -not $committed) {
        try {
            [System.IO.File]::WriteAllText($asmInfoPath, $asmOriginal, (New-Object System.Text.UTF8Encoding($true)))
            Warn "AssemblyInfo.cs restored."
        } catch { }
    }
    $exitCode = 1
}
finally {
    if (Test-Path $notesFile) { Remove-Item $notesFile -Force -ErrorAction SilentlyContinue }
    if ($transcriptOn) { try { Stop-Transcript | Out-Null } catch { } }
}

exit $exitCode