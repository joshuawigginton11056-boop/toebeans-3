<#
    Builds the scripted Blender props into Assets/GeneratedModels.

    Every prop under models\ is a Python script rather than a .blend file, on the same
    principle as the rest of this project's geometry: the terrain, the track and the
    volcano are all generators rebuilt from a seed, and a saved binary mesh is the one
    thing you cannot edit six months later. Run this and you get the same props you got
    last time, or a failure explaining why not.

    Nothing here touches the running Blender instance. Each model is built in its own
    headless process from factory settings, so a preference you changed while modelling
    cannot leak into a build.

        .\Tools\blender\build-models.ps1                    # every model, after verifying axes
        .\Tools\blender\build-models.ps1 -Model volcanic_rock
        .\Tools\blender\build-models.ps1 -SkipVerify        # when iterating on one prop

    Unity picks the results up on next focus. The FBX import settings that matter are
    already baked into the file by the exporter - see Tools\blender\toebeans_blender.py
    for why those particular settings and not the obvious ones.
#>

[CmdletBinding()]
param(
    [string] $Model,
    [switch] $SkipVerify,
    [string] $BlenderPath = $env:BLENDER_PATH
)

$ErrorActionPreference = 'Stop'
$started = Get-Date

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent (Split-Path -Parent $here)

# BLENDER_PATH is what the Blender MCP server reads too, so honouring it here keeps the
# two ways of reaching Blender pointed at one install.
if (-not $BlenderPath) {
    $BlenderPath = "C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe"
}
if (-not (Test-Path $BlenderPath)) {
    throw "Blender not found at '$BlenderPath'. Set BLENDER_PATH or pass -BlenderPath."
}

function Invoke-BlenderScript {
    param([string] $Script, [string] $Label)

    Write-Host ""
    Write-Host "=== $Label ===" -ForegroundColor Cyan

    $output = & $BlenderPath --background --factory-startup --python $Script 2>&1
    $ok = ($LASTEXITCODE -eq 0)

    # Blender is noisy on startup and quiet about what you asked for. Surface the lines
    # the scripts actually print, plus anything that looks like a failure.
    $output | Where-Object {
        $_ -match '^(BUILT|AXIS_VERIFY|  ok |  FAIL |  skip |\d\. )' -or
        $_ -match 'Error|Traceback|AssertionError|failed validation|^  - '
    } | ForEach-Object { Write-Host "  $_" }

    if (-not $ok) {
        Write-Host "  -> FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
    }
    return $ok
}

$results = @{}

if (-not $SkipVerify) {
    $results['verify_axes'] = Invoke-BlenderScript -Script (Join-Path $here 'verify_axes.py') -Label 'verify axes'
    if (-not $results['verify_axes']) {
        throw "Axis verification failed. Not building models against a broken export convention."
    }
}

$modelDir = Join-Path $here 'models'
$scripts = if ($Model) {
    $candidate = Join-Path $modelDir "$($Model -replace '\.py$','').py"
    if (-not (Test-Path $candidate)) { throw "No model script at $candidate" }
    @(Get-Item $candidate)
} else {
    @(Get-ChildItem $modelDir -Filter '*.py' | Where-Object { $_.Name -notlike '_*' })
}

if ($scripts.Count -eq 0) { throw "No model scripts found in $modelDir" }

foreach ($s in $scripts) {
    $results[$s.BaseName] = Invoke-BlenderScript -Script $s.FullName -Label $s.BaseName
}

$failed = @($results.GetEnumerator() | Where-Object { -not $_.Value })

Write-Host ""
Write-Host ("Built {0}/{1} in {2:n1}s -> Assets\GeneratedModels" -f `
    ($results.Count - $failed.Count), $results.Count, ((Get-Date) - $started).TotalSeconds)

if ($failed.Count -gt 0) {
    Write-Host ("Failed: " + ($failed.Name -join ', ')) -ForegroundColor Red
    exit 1
}
