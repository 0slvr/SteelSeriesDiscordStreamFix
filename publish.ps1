<#
    publish.ps1
    ------------
    One-click "publish new version to GitHub" script for
    SteelSeriesDiscordStreamFix, built on Velopack.

    Run this from Visual Studio's Tools > External Tools menu
    (see setup notes below), or directly:
        powershell -ExecutionPolicy Bypass -File .\publish.ps1

    What it does:
      1. Reads the last published version from version.txt (creates it
         with 1.0.0 if missing) and bumps the patch number automatically.
      2. Builds the app in Release mode (classic .NET Framework project,
         so we use `dotnet build`, not `dotnet publish -r win-x64`).
      3. Uses `vpk pack` to build the Velopack release + installer,
         with the app icon applied to Update.exe/Setup.exe/dialogs.
      4. Uses `vpk upload github` to publish it straight to GitHub
         Releases on your repo.

    Requirements (one-time setup):
      - dotnet tool install -g vpk
      - A GitHub Personal Access Token with "Contents: Read and write"
        on this repo, stored in an environment variable named GH_TOKEN.
        (System Properties > Environment Variables, or:
         setx GH_TOKEN "ghp_xxx...")
      - Update $repoUrl, $projectFile, and $iconFile below to match your
        repo/project.
#>

param(
    [string]$VersionOverride
)

$ErrorActionPreference = "Stop"

# ---- Configuration: edit these lines for your setup ----
$repoUrl     = "https://github.com/0slvr/SteelSeriesDiscordStreamFix"
$projectFile = "SteelSeriesDiscordStreamFix.csproj"   # adjust if your .csproj has a different name
$exeName     = "SteelSeriesDiscordStreamFix.exe"
$appId       = "SteelSeriesDiscordStreamFix"
$iconFile    = "steelseries.ico"                      # applied to Update.exe, Setup.exe, and installer dialogs
# --------------------------------------------------------------

$root        = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionFile = Join-Path $root "version.txt"
$publishDir  = Join-Path $root "publish"
$iconPath    = Join-Path $root $iconFile

if (-not $env:GH_TOKEN) {
    Write-Error "GH_TOKEN environment variable is not set. Create a GitHub token and run: setx GH_TOKEN `"ghp_xxx`" then reopen your terminal."
    exit 1
}

if (-not (Test-Path $iconPath)) {
    Write-Error "Icon file not found at '$iconPath'. Update `$iconFile at the top of this script, or place $iconFile next to publish.ps1."
    exit 1
}

# --- Determine version ---
if ($VersionOverride) {
    $version = $VersionOverride
}
else {
    if (Test-Path $versionFile) {
        $current = (Get-Content $versionFile -Raw).Trim()
    }
    else {
        $current = "1.0.0"
    }

    $parts = $current.Split(".")
    if ($parts.Length -ne 3) { $parts = @("1", "0", "0") }
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2] + 1
    $version = "$major.$minor.$patch"
}

Write-Host "==> Publishing version $version" -ForegroundColor Cyan

# --- 1. Build ---
Write-Host "==> Running dotnet build..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet build (Join-Path $root $projectFile) -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed."; exit 1 }

# --- 2. Pack with Velopack ---
Write-Host "==> Packing with vpk..." -ForegroundColor Cyan
vpk pack -u $appId -v $version -p $publishDir --mainExe $exeName --icon $iconPath
if ($LASTEXITCODE -ne 0) { Write-Error "vpk pack failed."; exit 1 }

# --- 3. Upload to GitHub Releases ---
Write-Host "==> Uploading release to GitHub..." -ForegroundColor Cyan
vpk upload github `
    --repoUrl $repoUrl `
    --token $env:GH_TOKEN `
    --publish `
    --tag "v$version" `
    --releaseName "v$version"
if ($LASTEXITCODE -ne 0) { Write-Error "vpk upload failed."; exit 1 }

# --- 4. Save the new version for next time ---
Set-Content -Path $versionFile -Value $version -NoNewline

Write-Host "==> Done! Version $version is now live on GitHub Releases." -ForegroundColor Green
Write-Host "    Existing installs will pick it up automatically next launch (AppUpdater.CheckAndApplyAsync)." -ForegroundColor Green