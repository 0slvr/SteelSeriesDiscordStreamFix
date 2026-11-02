<#
    publish-button.ps1 - one-click release for Visual Studio.
    Run from the "Publish Release" launch profile (Ctrl+F5). It asks once, runs publish.ps1 (which bumps the
    version, builds, packs, commits + pushes, and creates the GitHub releases), then shows the result in a box.
    Extra arguments are passed to publish.ps1, e.g.  -Notes "Fixed mic bug"  or  -Bump minor.
#>
param([Parameter(ValueFromRemainingArguments = $true)] [string[]]$PublishArgs)

Add-Type -AssemblyName System.Windows.Forms
$root = $PSScriptRoot
Set-Location $root

function Box([string]$text, [string]$title, [string]$buttons = "OK", [string]$icon = "Information") {
    return [System.Windows.Forms.MessageBox]::Show($text, $title, $buttons, $icon)
}

# ---- Safety checks (do NOT publish from a broken git state) ----
$problems = @()
$gitDir = Join-Path $root ".git"
if ((Test-Path (Join-Path $gitDir "rebase-merge")) -or (Test-Path (Join-Path $gitDir "rebase-apply"))) {
    $problems += "A git rebase is unfinished. Run 'git rebase --abort' (or --continue) first."
}
if (Test-Path (Join-Path $gitDir "MERGE_HEAD")) { $problems += "A git merge is unfinished. Finish or abort it first." }
$branch = (& git rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1)
if ($branch -eq "HEAD") { $problems += "Git is on a detached HEAD. Run 'git checkout master' first." }
if (& git ls-files -u 2>$null) { $problems += "There are unresolved merge conflicts (git status shows UU)." }
if (-not $env:GH_TOKEN) { $problems += "GH_TOKEN is not set (setx GH_TOKEN ""ghp_xxx"", then restart Visual Studio)." }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { $problems += "vpk is missing (dotnet tool install -g vpk)." }
if ($problems) {
    Box ("Not publishing:`n`n- " + ($problems -join "`n- ")) "Publish blocked" "OK" "Warning" | Out-Null
    exit 1
}

# ---- Confirm ----
$ver = (Get-Content (Join-Path $root "version.txt") -Raw).Trim()
$ask = Box "Publish a new release to GitHub for ALL users?`n`nCurrent version: $ver (the next one is picked automatically).`nOnly continue if this build was tested." "Publish Release" "YesNo" "Question"
if ($ask -ne "Yes") { exit 2 }

# ---- Run publish.ps1 (hidden console, we wait for it and capture everything it prints) ----
$ErrorActionPreference = "Continue"
$raw  = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "publish.ps1") @PublishArgs 2>&1
$code = $LASTEXITCODE
if ($null -eq $code) { $code = -1 }
$lines = @($raw | ForEach-Object { "$_" } | Where-Object { $_.Trim() })
$tail  = ($lines | Select-Object -Last 14) -join "`n"
if (-not $tail) { $tail = "(publish.ps1 printed nothing - it probably did not start)" }

if ($code -eq 0) {
    Box "Published!`n`n$tail" "Publish Release - done" "OK" "Information" | Out-Null
}
else {
    Box "Publish FAILED (exit code $code).`n`n$tail" "Publish Release - FAILED" "OK" "Error" | Out-Null
}
exit $code
