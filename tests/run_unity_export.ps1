<#
.SYNOPSIS
Runs the exporter on avatars in a Unity project without opening the Editor: a whole avatar pack
(-Mode pack, the default), or every animation posed (-Mode poses). See tests/UNITY-EXPORT.md.

.DESCRIPTION
Builds the exporter's .unitypackage from this repository, imports it into the Unity project (it lands in
Assets/FactorioAvatarExporter, as an import by hand would), then has Unity render the avatars in batch
mode: a prefab, an object in a scene, or every humanoid avatar at the top level of a scene. The
sprites, raw renders and reports, and Unity's logs, end up in -Out. The project must not be open in Unity.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tests\run_unity_export.ps1 -Project "C:\Users\me\VRChat\MyAvatar" -Scene "Assets/Scenes/Avatar.unity"
.EXAMPLE
powershell -ExecutionPolicy Bypass -File tests\run_unity_export.ps1 -Project "C:\Users\me\VRChat\MyAvatar" -Avatar "Assets/Mika/Mika.prefab"
#>
param(
    # The Unity project folder (the one holding Assets\ and ProjectSettings\).
    [Parameter(Mandatory = $true)][string]$Project,
    # A scene in the project (e.g. Assets/Scenes/Avatar.unity): renders every active humanoid avatar at its top level, or -Avatar.
    [string]$Scene = "",
    # A prefab in the project (Assets/....prefab), or the name of a top-level object in -Scene.
    [string]$Avatar = "",
    # Unity.exe; default: the Unity Hub install of the project's editor version.
    [string]$Unity = "",
    # Where the results go; default unity-export\<date-time> in the repository (ignored by git).
    [string]$Out = "",
    # Don't import the package (it is already in the project, e.g. imported by hand).
    [switch]$SkipImport,
    # pack: the avatar pack (mod zip) itself, with report.txt and a preview sheet per animation;
    # poses: every animation posed by the exporter, a sheet each.
    [ValidateSet("pack", "poses")][string]$Mode = "pack"
)
$ErrorActionPreference = "Stop"
function Quote([string]$s) { '"' + $s + '"' }
function PathOf { [IO.Path]::Combine([string[]]$args) }
$repo = Split-Path -Parent $PSScriptRoot

$Project = (Resolve-Path $Project).Path
if (-not (Test-Path (PathOf $Project "Assets"))) { throw "$Project is not a Unity project (no Assets folder)" }
if (Test-Path (PathOf $Project "Temp" "UnityLockfile")) {
    Write-Warning "the project looks open in Unity (Temp\UnityLockfile exists); close it first, or Unity will refuse to start"
}
if (-not $Scene -and -not $Avatar) { throw "pass -Scene and/or -Avatar" }
if (-not $Unity) {
    $versionFile = PathOf $Project "ProjectSettings" "ProjectVersion.txt"
    $version = (Select-String -Path $versionFile -Pattern "m_EditorVersion: (\S+)").Matches[0].Groups[1].Value
    $Unity = PathOf $env:ProgramFiles "Unity" "Hub" "Editor" $version "Editor" "Unity.exe"
}
if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity; pass -Unity <path to Unity.exe>" }
if (-not $Out) { $Out = PathOf $repo "unity-export" (Get-Date -Format "yyyy-MM-dd-HHmm") }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path

function Run-Unity([string[]]$arguments, [string]$log) {
    $all = @("-batchmode", "-projectPath", (Quote $Project), "-logFile", (Quote $log)) + $arguments
    $p = Start-Process -FilePath $Unity -ArgumentList $all -Wait -PassThru
    return $p.ExitCode
}

if (-not $SkipImport) {
    $python = @("py", "python", "python3") | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
    if (-not $python) { throw "Python is needed to build the .unitypackage (or import it by hand and pass -SkipImport)" }
    # py follows a script's "#!/usr/bin/env python3" line, which can find the Microsoft Store's python3
    $pyArgs = @(if ($python -eq "py") { "-3" })
    $package = (& $python @pyArgs (PathOf $repo "tools" "make_unitypackage.py") --out $Out | Select-Object -Last 1).Trim()
    Write-Host "== importing $package into $Project"
    $code = Run-Unity @("-importPackage", (Quote $package), "-quit") (PathOf $Out "unity-import.log")
    if ($code -ne 0) { throw "the import failed (exit $code); see $Out\unity-import.log" }
}

Write-Host "== rendering ($Mode; Unity runs without a window; this takes a few minutes)"
$method = if ($Mode -eq "poses") { "ExporterCommandLine.RenderPoses" } else { "ExporterCommandLine.ExportPacks" }
$render = @("-executeMethod", "FactorioAvatarExporter.Editor.$method", "-exportOut", (Quote $Out))
if ($Scene) { $render += @("-exportScene", (Quote $Scene)) }
if ($Avatar) { $render += @("-exportAvatar", (Quote $Avatar)) }
$code = Run-Unity ($render + @("-quit")) (PathOf $Out "unity-render.log")
$summary = PathOf $Out "summary.txt"
if (Test-Path $summary) { Get-Content $summary | Write-Host }
if ($code -ne 0 -or -not (Test-Path $summary)) {
    Write-Warning "the render failed (exit $code); see $Out\unity-render.log (search for 'error' and 'Exception')"
    exit 1
}
Write-Host "== results in $Out"
