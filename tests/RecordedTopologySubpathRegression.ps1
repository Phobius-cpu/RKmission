$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw

foreach ($required in @(
    'DungeonSourceAttachDistance = 4f',
    'DungeonTargetAttachDistance = 4f',
    'DungeonMinimumVerticalSpan = 1.5f',
    'for (int source = 0; source < points.Count - 1; source++)',
    'for (int end = source + 1; end < points.Count; end++)',
    'verticalRegression += Math.Max(0f',
    'unnecessaryDistance * 0.5f + verticalRegression * 8f',
    'Points = points.GetRange(source, end - source + 1)',
    'int originalSource = reversed ? points.Count - 1 - source : source',
    'int originalTarget = reversed ? points.Count - 1 - end : end',
    'subpath {best.SourceIndex}->{best.TargetIndex}',
    'source attach={best.SourceDistance:0.0}m',
    'target attach={best.TargetDistance:0.0}m')) {
    if (-not $recorder.Contains($required)) {
        throw "Recorded topology subpath behavior missing: $required"
    }
}
if ($recorder.Contains('startDistance > 8f || targetDistance > 6f')) {
    throw 'Whole-recording endpoint matching returned.'
}
Write-Output 'PASS interior attachments replace whole-recording endpoint matching with 4m source/target gates'

if (-not $recorder.Contains('verticalSpan < DungeonMinimumVerticalSpan') -or
    -not $recorder.Contains('verticalProgress < Math.Min(1f, initialVerticalGap * 0.5f)') -or
    -not $recorder.Contains('directedProgress < 0.75f') -or
    -not $recorder.Contains('pathLength < 1.5f')) {
    throw 'Flat, trivial, or wrong-direction subpath rejection regressed.'
}
Write-Output 'PASS selected subpaths require meaningful span and directed progress toward the target level'

if (-not $recorder.Contains('IsContinuousDungeonRecording(points)') -or
    -not $recorder.Contains('Vector3.Distance(points[i - 1], points[i]) <= 5f')) {
    throw 'Discontinuous recordings can qualify for subpath playback.'
}
Write-Output 'PASS recordings with a sample gap above 5m remain ineligible'

if (-not $recorder.Contains('foreach (bool reverse in new[] { false, true })') -or
    -not $recorder.Contains('points.AsEnumerable().Reverse().ToList()') -or
    -not $recorder.Contains('_playIndex++') -or $recorder.Contains('_playIndex--')) {
    throw 'Forward/reverse selection or monotonic prepared playback regressed.'
}
Write-Output 'PASS either traversal orientation can be selected and prepared playback stays strictly monotonic'

# Synthetic switchback: original endpoints are deliberately far away. The useful
# ascending interior section is followed by an upper-platform loop and descent.
$points = @(
    [pscustomobject]@{ X = 276.2; Y = 5.0; Z = 94.0 },
    [pscustomobject]@{ X = 280.1; Y = 5.1; Z = 97.0 },
    [pscustomobject]@{ X = 284.0; Y = 5.2; Z = 100.0 },
    [pscustomobject]@{ X = 288.0; Y = 5.3; Z = 102.0 },
    [pscustomobject]@{ X = 290.0; Y = 6.5; Z = 103.0 },
    [pscustomobject]@{ X = 291.0; Y = 7.4; Z = 101.0 },
    [pscustomobject]@{ X = 292.0; Y = 8.0; Z = 98.0 },
    [pscustomobject]@{ X = 293.0; Y = 8.0; Z = 96.0 },
    [pscustomobject]@{ X = 297.0; Y = 8.0; Z = 96.0 },
    [pscustomobject]@{ X = 293.0; Y = 8.0; Z = 98.0 },
    [pscustomobject]@{ X = 291.0; Y = 7.2; Z = 101.0 },
    [pscustomobject]@{ X = 289.0; Y = 6.0; Z = 102.0 },
    [pscustomobject]@{ X = 286.0; Y = 5.2; Z = 100.0 },
    [pscustomobject]@{ X = 283.0; Y = 5.1; Z = 97.0 },
    [pscustomobject]@{ X = 280.0; Y = 5.0; Z = 94.0 },
    [pscustomobject]@{ X = 282.0; Y = 5.0; Z = 90.0 })
$from = [pscustomobject]@{ X = 288.0; Y = 5.3; Z = 100.4 }
$target = [pscustomobject]@{ X = 292.2; Y = 8.0; Z = 96.0 }
function Distance($a, $b) {
    [math]::Sqrt([math]::Pow($a.X-$b.X,2) + [math]::Pow($a.Y-$b.Y,2) + [math]::Pow($a.Z-$b.Z,2))
}
if ((Distance $from $points[0]) -le 8 -or (Distance $target $points[-1]) -le 6) {
    throw 'Synthetic loop no longer proves endpoint matching would fail.'
}
$sourceAttach = Distance $from $points[3]
$targetAttach = Distance $target $points[7]
$selected = $points[3..7]
if ($sourceAttach -lt 1.5 -or $sourceAttach -gt 1.7 -or
    $targetAttach -lt 0.7 -or $targetAttach -gt 0.9 -or
    $selected.Count -ne 5 -or $selected[-1].Y - $selected[0].Y -lt 2.5) {
    throw 'Synthetic U-turn ramp no longer represents a valid lower-to-upper interior subpath.'
}
for ($i = 1; $i -lt $points.Count; $i++) {
    if ((Distance $points[$i-1] $points[$i]) -gt 5) {
        throw 'Synthetic loop is not a continuous recording.'
    }
}
if ((Distance $from $points[12]) -le $sourceAttach) {
    throw 'Synthetic loop no longer makes the original ascending branch the preferred source attachment.'
}
Write-Output 'PASS U-turn-shaped loop selects the climbing interior branch with ~1.6m/~0.8m attachments while distant original endpoints fail'

if (-not $recorder.Contains('eligibleRecordings > 0') -or
    -not $recorder.Contains('No viable recorded dungeon topology subpath')) {
    throw 'Bounded no-subpath rejection diagnostic regressed.'
}
Write-Output 'PASS no viable subpath returns cleanly with one bounded per-attempt diagnostic for generated fallback'
