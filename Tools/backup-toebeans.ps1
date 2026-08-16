<#
    Backs up the toebeans-3 Unity project to an external drive.

    Written on 2026-08-15, the day a sculpted terrain heightmap was lost. The cause was not
    a missing commit - it was relying on version control as the only copy. Two things made
    that fatal: the terrain had never been committed at all (it sat untracked for weeks),
    and when it finally was, a .gitattributes rule silently stripped bytes out of it. No
    version control system protects a file you have not committed, so this runs beside git
    rather than instead of it.

    Two layers, because they fail differently:

      current\    A full mirror of the project minus regenerable folders. Answers "my drive
                  died" and "I deleted something an hour ago". Mirrored, so it always
                  matches the working tree - including mistakes, once it next runs.

      snapshots\  Dated zips of the small, irreplaceable set: scenes, project settings and
                  the hand-written generators. Answers "I broke something three days ago
                  and only noticed now", which the mirror cannot. Kept small on purpose so
                  many can be retained.

    Nothing here deletes anything in the project. It only reads.
#>

[CmdletBinding()]
param(
    [string] $Source      = "C:\Users\Work\Documents\GitHub\toebeans-3",
    [string] $Destination = "E:\Backups\toebeans-3",
    [int]    $KeepSnapshots = 30
)

$ErrorActionPreference = 'Stop'
$started = Get-Date

# Regenerable or machine-specific: Unity rebuilds these from Assets on next open, and they
# are the bulk of the project on disk (Library alone is larger than everything worth saving).
$ExcludeDirs = @(
    'Library', 'Temp', 'Obj', 'obj', 'Logs', 'UserSettings',
    'Build', 'Builds', '.vs', 'MemoryCaptures', 'Recordings', 'node_modules'
)

# The irreplaceable set: authored by hand, small, and impossible to regenerate. The store
# asset packs are deliberately absent - they are gigabytes and re-downloadable.
$SnapshotPaths = @(
    'Assets\Scenes',
    'Assets\RockBridge', 'Assets\PlayerPath', 'Assets\Cave', 'Assets\RaceTrack',
    'Assets\Volcano', 'Assets\LowPolyTerrain', 'Assets\Kart', 'Assets\FrozenLake',
    'Assets\Barriers', 'Assets\GeneratedTrees', 'Assets\LavaFlow', 'Assets\LavaPond',
    'Assets\Editor', 'Assets\Scripts', 'Assets\Prefabs', 'Assets\Shaders',
    'ProjectSettings', 'Packages',
    '.gitattributes', '.gitignore'
)

function Write-Log([string] $Message) {
    $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $Message
    Write-Host $line
    if ($script:LogFile) { Add-Content -Path $script:LogFile -Value $line -Encoding utf8 }
}

if (-not (Test-Path $Source)) { throw "Source not found: $Source" }

$driveRoot = Split-Path -Qualifier $Destination
if (-not (Test-Path $driveRoot)) {
    throw "Backup drive $driveRoot is not available. Plug it in, or pass -Destination."
}

$current   = Join-Path $Destination 'current'
$snapshots = Join-Path $Destination 'snapshots'
foreach ($d in @($Destination, $current, $snapshots)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

$script:LogFile = Join-Path $Destination 'backup.log'
Write-Log "=== backup started ==="
Write-Log "source      $Source"
Write-Log "destination $Destination"

# ---------------------------------------------------------------- 1. full mirror
# /MIR mirrors, so deletions propagate. That is intended: this layer answers "restore the
# project as it is", and the snapshots below cover "restore it as it was".
$xd = @()
foreach ($d in $ExcludeDirs) { $xd += '/XD'; $xd += (Join-Path $Source $d) }

Write-Log "mirroring (excluding: $($ExcludeDirs -join ', ')) ..."
$roboArgs = @($Source, $current, '/MIR', '/R:1', '/W:1', '/MT:8', '/NFL', '/NDL', '/NP', '/NJH') + $xd
& robocopy.exe @roboArgs | Out-Null
$code = $LASTEXITCODE

# Robocopy: 0-7 are success (bits mean copied/extra/mismatched), 8+ are real failures.
if ($code -ge 8) { Write-Log "ROBOCOPY FAILED with exit code $code"; throw "robocopy failed ($code)" }
Write-Log "mirror ok (robocopy code $code)"

# ---------------------------------------------------------------- 2. dated snapshot
$stamp   = Get-Date -Format 'yyyy-MM-dd_HHmm'
$zipPath = Join-Path $snapshots "toebeans-3_$stamp.zip"

$existing = @()
foreach ($rel in $SnapshotPaths) {
    $full = Join-Path $Source $rel
    if (Test-Path $full) { $existing += $full } else { Write-Log "  (skipped, not present: $rel)" }
}

if ($existing.Count -gt 0) {
    Write-Log "writing snapshot $([System.IO.Path]::GetFileName($zipPath)) ..."
    Compress-Archive -Path $existing -DestinationPath $zipPath -CompressionLevel Optimal -Force
    $mb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Log "snapshot ok ($mb MB)"
} else {
    Write-Log "WARNING: no snapshot paths found - nothing archived"
}

# Prune oldest snapshots, keeping the most recent $KeepSnapshots.
$all = Get-ChildItem $snapshots -Filter 'toebeans-3_*.zip' | Sort-Object LastWriteTime -Descending
if ($all.Count -gt $KeepSnapshots) {
    $old = $all | Select-Object -Skip $KeepSnapshots
    foreach ($f in $old) { Remove-Item $f.FullName -Force; Write-Log "pruned $($f.Name)" }
}

# ---------------------------------------------------------------- 3. report
$mirrorGB = [math]::Round((Get-ChildItem $current -Recurse -File -ErrorAction SilentlyContinue |
                Measure-Object Length -Sum).Sum / 1GB, 2)
$free = (Get-PSDrive -Name $driveRoot.TrimEnd(':')).Free / 1GB

Write-Log ("mirror size {0} GB | snapshots kept {1} | free on {2} {3:N1} GB | took {4:N0}s" -f `
    $mirrorGB, (Get-ChildItem $snapshots -Filter '*.zip').Count, $driveRoot, $free,
    ((Get-Date) - $started).TotalSeconds)
Write-Log "=== backup finished ==="

# Robocopy sets $LASTEXITCODE to a bitmask where 1 means "files were copied" - success, not
# failure. Left alone it becomes this script's exit code and Task Scheduler reports the job
# as failed every time it actually did something. Any real failure has already thrown above.
exit 0
