<#
    publish.ps1  (full version)
    ---------------------------
    One-click release for SteelSeriesDiscordStreamFix, built on Velopack.

    Usage (Visual Studio External Tools button, or terminal):
        powershell -ExecutionPolicy Bypass -File .\publish.ps1
        .\publish.ps1 -Bump minor                  # 1.0.18 -> 1.1.0
        .\publish.ps1 -Notes "Fixed mic mute bug"  # custom release notes + commit message
        .\publish.ps1 -VersionOverride 2.0.0       # force an exact version
        .\publish.ps1 -DryRun                      # build + pack only, no git push, no upload
        .\publish.ps1 -SkipGit                     # do not commit/push source code
        .\publish.ps1 -UploadOnly -VersionOverride 1.0.19   # retry upload of an already packed version

    Order of operations:
      0. Preflight checks (tools, token, icon, git state, .gitignore)
      1. Pick the next version (max of version.txt and git tags, then bump)
      2. Build (Release)
      3. Download previous release from GitHub (so delta packages work on any PC)
      4. Pack with vpk
      5. Commit + push source code (so the release tag points at the new code)
      6. Upload release to GitHub
      7. Save version.txt

    Requirements:
      - dotnet SDK, git, and:  dotnet tool install -g vpk
      - Environment variable GH_TOKEN (GitHub token, "Contents: Read and write")
        setx GH_TOKEN "ghp_xxx"   (then restart Visual Studio)
#>

[CmdletBinding()]
param(
    [string]$VersionOverride,
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch",
    [string]$Notes,
    [switch]$SkipGit,
    [switch]$UploadOnly,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ---------------- Configuration ----------------
$repoUrl     = "https://github.com/0slvr/SteelSeriesDiscordStreamFix"
$projectFile = "SteelSeriesDiscordStreamFix.csproj"
$exeName     = "SteelSeriesDiscordStreamFix.exe"
$appId       = "SteelSeriesDiscordStreamFix"
$iconFile    = "steelseries.ico"
$updateAssemblyInfo = $true     # also write the version into Properties\AssemblyInfo.cs if it exists
# -----------------------------------------------

$root        = $PSScriptRoot
$versionFile = Join-Path $root "version.txt"
$publishDir  = Join-Path $root "publish"
$releasesDir = Join-Path $root "Releases"
$logsDir     = Join-Path $root "logs"
$iconPath    = Join-Path $root $iconFile
$asmInfoPath = Join-Path $root "Properties\AssemblyInfo.cs"
$notesFile   = Join-Path $env:TEMP "release-notes-$appId.md"

# ---------------- Helpers ----------------
function Step([string]$m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Info([string]$m) { Write-Host "    $m" -ForegroundColor Gray }
function Warn([string]$m) { Write-Host "    [!] $m" -ForegroundColor Yellow }

# Run a native command, show its output, throw on failure (unless -AllowFail).
function Run {
    param([string]$Exe, [string[]]$Arguments, [switch]$AllowFail)
    & $Exe @Arguments | Out-Host
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not $AllowFail) {
        throw "'$Exe $($Arguments[0])' failed (exit code $code)."
    }
    return $code
}

# Run git inside the project folder and capture output quietly.
function GitOut {
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { $o = & git -C $root @args 2>$null; return $o }
    finally { $ErrorActionPreference = $old }
}

function Require-Command([string]$name, [string]$hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "'$name' was not found. $hint"
    }
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
    if ($useGit) { Require-Command "git" "Install Git for Windows." }

    if (-not $env:GH_TOKEN) {
        throw "GH_TOKEN is not set. Run: setx GH_TOKEN `"ghp_xxx`"  then restart Visual Studio."
    }
    if (-not (Test-Path (Join-Path $root $projectFile))) { throw "Project file not found: $projectFile" }
    if (-not (Test-Path $iconPath)) { throw "Icon file not found: $iconPath" }

    $branch = $null
    if ($useGit) {
        $inside = GitOut rev-parse --is-inside-work-tree
        if ($inside -ne "true") { throw "This folder is not a git repository. Use -SkipGit or run 'git init'." }

        $branch = (GitOut rev-parse --abbrev-ref HEAD | Select-Object -First 1)
        if ($branch -eq "HEAD") { throw "Git is in a detached HEAD state. Checkout a branch first." }
        Info "Git branch: $branch"

        Info "Fetching tags from remote..."
        GitOut fetch --tags --quiet | Out-Null

        # Make sure build output / logs never get committed.
        $gitignorePath = Join-Path $root ".gitignore"
        $needed = @("publish/", "Releases/", "logs/", "bin/", "obj/", ".vs/")
        $existing = @()
        if (Test-Path $gitignorePath) { $existing = Get-Content $gitignorePath }
        $missing = $needed | Where-Object { $existing -notcontains $_ -and $existing -notcontains "/$_" }
        if ($missing) {
            $block = @("", "# added by publish.ps1") + $missing
            Add-Content -Path $gitignorePath -Value $block
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

    Write-Host "==> Publishing version $version (previous: $latest)" -ForegroundColor Green
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
        Step "Downloading previous release for delta packages"
        $dl = Run vpk @("download", "github", "--repoUrl", $repoUrl, "--token", $env:GH_TOKEN, "-o", $releasesDir) -AllowFail
        if ($dl -ne 0) { Warn "Could not download the previous release (first release or offline). Continuing without deltas." }

        # ---------------- 4. Pack ----------------
        Step "Packing with vpk"
        Run vpk @("pack", "-u", $appId, "-v", $version, "-p", $publishDir, "--mainExe", $exeName,
                  "--icon", $iconPath, "-o", $releasesDir, "--releaseNotes", $notesFile) | Out-Null

        # ---------------- 5. Source code ----------------
        Set-Content -Path $versionFile -Value $version -NoNewline

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
            Info "Dry run: skipped git commit/push."
        }
    }

    # ---------------- 6. Upload ----------------
    if ($DryRun) {
        Step "Dry run finished. Packages are in: $releasesDir"
        if (-not $UploadOnly -and (Test-Path $versionFile)) {
            # Dry run must not consume the version number.
            if ($latest) { Set-Content -Path $versionFile -Value $latest.ToString() -NoNewline }
        }
    }
    else {
        Step "Uploading release to GitHub"
        try {
            Run vpk @("upload", "github", "--repoUrl", $repoUrl, "--token", $env:GH_TOKEN,
                      "--publish", "--tag", "v$version", "--releaseName", "v$version",
                      "-o", $releasesDir) | Out-Null
        }
        catch {
            Warn "Upload failed, but the code is already pushed and packages are ready."
            Warn "Retry with:  .\publish.ps1 -UploadOnly -VersionOverride $version"
            throw
        }

        Write-Host ""
        Write-Host "==> Done! Version $version is now live." -ForegroundColor Green
        Write-Host "    $repoUrl/releases/tag/v$version" -ForegroundColor Green
        Write-Host "    Existing installs will update on next launch (AppUpdater.CheckAndApplyAsync)." -ForegroundColor Green
    }

    $stopwatch.Stop()
    Info ("Total time: {0:N1}s" -f $stopwatch.Elapsed.TotalSeconds)
    $exitCode = 0
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red

    # Undo local AssemblyInfo change if nothing was committed.
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