<#
.SYNOPSIS
Plays the in-game screenshot checks in the real game and collects the results. See tests/INGAME-CHECKS.md.

.DESCRIPTION
Starts Factorio with the mod from this repository, the screenshot test mod
(tests/vrc-avatars-screenshots) and avatar packs, in its own temporary folder (your saves, mods
and settings are not touched). The game plays the scenes by itself and closes when they are done,
in a few minutes. The screenshots, manifest.json, the game log, GIFs and REPORT.md end up in -Out.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tests\run_ingame_checks.ps1
.EXAMPLE
powershell -ExecutionPolicy Bypass -File tests\run_ingame_checks.ps1 -Pack build\harness\vrc-avatar-harness-generic_1.0.0.zip
#>
param(
    # The Factorio install folder (the one holding bin\ and data\).
    [string]$Factorio = "C:\Program Files (x86)\Steam\steamapps\common\Factorio",
    # Avatar packs (folders or zips) to include. Without any, a synthetic test pack is made (needs Python with Pillow).
    [string[]]$Pack = @(),
    # Leave Space Age, Quality and Elevated Rails off even if installed (no flight scene then).
    [switch]$NoSpaceAge,
    # Where the results go; default ingame-checks\<date-time> in the repository.
    [string]$Out = "",
    [int]$TimeoutMinutes = 10
)
$ErrorActionPreference = "Stop"
function Quote([string]$s) { '"' + $s + '"' }
function PathOf { [IO.Path]::Combine([string[]]$args) }
$repo = Split-Path -Parent $PSScriptRoot
$exe = PathOf $Factorio "bin" "x64" "factorio.exe"
if (-not (Test-Path $exe)) { throw "factorio.exe not found at $exe; pass -Factorio <install folder>" }
if (-not $Out) { $Out = PathOf $repo "ingame-checks" (Get-Date -Format "yyyy-MM-dd-HHmm") }
$dlc = (-not $NoSpaceAge) -and (Test-Path (PathOf $Factorio "data" "space-age"))
$python = @("py", "python", "python3") | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
# py follows a script's "#!/usr/bin/env python3" line, which can find the Microsoft Store's python3
# placeholder instead of Python; -3 makes it use the installed Python 3
$pyArgs = @(if ($python -eq "py") { "-3" })


$work = Join-Path ([IO.Path]::GetTempPath()) ("avatar-checks-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$mods = Join-Path $work "mods"
New-Item -ItemType Directory -Force -Path $mods, (PathOf $work "config") | Out-Null
Write-Host "== working in $work"

# The game reads its data from the install and writes everything (mods, saves, script-output, log) to $work.
$config = PathOf $work "config" "config.ini"
"[path]`r`nread-data=__PATH__executable__/../../data`r`nwrite-data=$($work -replace '\\', '/')`r`n" |
    Set-Content -Encoding ASCII $config

Copy-Item -Recurse (Join-Path $repo "vrc-avatars") (Join-Path $mods "vrc-avatars")
Copy-Item -Recurse (PathOf $repo "tests" "vrc-avatars-screenshots") (Join-Path $mods "vrc-avatars-screenshots")
if ($Pack.Count -gt 0) {
    foreach ($p in $Pack) { Copy-Item -Recurse (Resolve-Path $p) $mods }
} else {
    if ($python) {
        & $python @pyArgs (PathOf $repo "tools" "avatar_pack" "make_test_pack.py") --out $mods --id test --flight --tint | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "could not make the test pack (pip install pillow); going on without it" }
    } else {
        Write-Warning "Python not found: no test pack in the checks (install Python with Pillow, or pass -Pack)"
    }
    # a real pack from the exporter, drawn by its pack harness (no Unity needed)
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Write-Host "== building the harness pack"
        & dotnet run --project (PathOf $repo "unity" "dotnet" "PackHarness") -c Release -- `
            --out $mods --id harness-generic --title "Harness generic" --rig generic | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "could not build the harness pack; going on without it" }
    } else {
        Write-Warning ".NET SDK not found: no harness pack in the checks (install the .NET 8 SDK, or pass -Pack)"
    }
}

# mod-list.json: base, the DLC (or not) and every mod in the folder
$names = foreach ($item in Get-ChildItem $mods) {
    if ($item.PSIsContainer) { (Get-Content (Join-Path $item.FullName "info.json") -Raw | ConvertFrom-Json).name }
    elseif ($item.Extension -eq ".zip") { $item.BaseName -replace '_[0-9.]+$', '' }
}
$list = @(@{ name = "base"; enabled = $true })
$list += foreach ($n in "space-age", "quality", "elevated-rails") { @{ name = $n; enabled = $dlc } }
$list += foreach ($n in $names) { @{ name = $n; enabled = $true } }
@{ mods = $list } | ConvertTo-Json -Depth 4 | Set-Content -Encoding ASCII (Join-Path $mods "mod-list.json")

$hidden = if ($env:OS -eq "Windows_NT") { @{ WindowStyle = "Hidden" } } else { @{} }
$common = @("--config", (Quote $config), "--mod-directory", (Quote $mods))
$save = Join-Path $work "checks.zip"
Write-Host "== creating the test map"
$create = Start-Process -FilePath $exe -ArgumentList ($common + @("--create", (Quote $save))) -Wait -PassThru @hidden
if (-not (Test-Path $save)) {
    throw "the game could not create the test map (exit $($create.ExitCode)); see $work\factorio-current.log"
}

Write-Host "== playing the scenes (the game window opens and closes by itself)"
# Started outside Steam, the Steam build of the game exits at once and asks Steam to start it again,
# without the test's arguments; with Factorio's app id in the environment it runs as started.
$env:SteamAppId = "427520"
$env:SteamGameId = "427520"
$done = PathOf $work "script-output" "avatar-checks" "done.txt"
$game = Start-Process -FilePath $exe -PassThru -ArgumentList ($common + @(
    "--load-game", (Quote $save), "--disable-audio", "--window-size", "1280x720"))
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while (-not (Test-Path $done) -and -not $game.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
Start-Sleep -Seconds 3 # the last screenshots are written after done.txt is queued
if (-not $game.HasExited) { Stop-Process -Id $game.Id -Force }

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$results = PathOf $work "script-output" "avatar-checks"
if (Test-Path $results) { Copy-Item -Recurse -Force (Join-Path $results "*") $Out }
Copy-Item -Force (Join-Path $work "factorio-current.log") (Join-Path $Out "factorio.log") -ErrorAction SilentlyContinue
if (-not (Test-Path (Join-Path $Out "done.txt"))) {
    Write-Warning ("the scenes did not finish; see $Out\factorio.log. If the game closed at once and Steam " +
        "started it again without the test, start Steam first and try again.")
    exit 1
}
if ($python) {
    & $python @pyArgs (PathOf $repo "tools" "ingame_contact_sheet.py") $Out
} else {
    Write-Warning "Python not found: no GIFs or REPORT.md (run tools\ingame_contact_sheet.py later)"
}
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
Write-Host "== results in $Out"
