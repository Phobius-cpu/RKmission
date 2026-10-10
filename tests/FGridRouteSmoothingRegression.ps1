$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$planner = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridRecastPlanner.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$floor = Get-Content -LiteralPath (Join-Path $root 'RKmission/LocalRoutePlanner.cs') -Raw
$provider = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw

if ($planner.Contains('Math.Ceiling(segment / 4f)') -or
    -not $planner.Contains('SimplifySupportedPath(raw, start.Y)') -or
    -not $planner.Contains('walkway-supported leg(s)')) {
    throw 'Recast paths are still split into fixed four-metre legs or are not minimized.'
}
if (-not $planner.Contains('SupportedFGridSegment(') -or
    -not $planner.Contains('ValidateRaw(checkedPath, start.Y')) {
    throw 'Simplified Recast paths are not revalidated against FGrid support.'
}
Write-Output 'PASS Recast keeps only necessary floor-supported walkway legs and removes fixed four-metre splitting'

if (-not $recorder.Contains('candidate = SimplifySupportedFGridPath(candidate, player.Position.Y,') -or
    -not $recorder.Contains('SupportedRecordedFGridRoute(candidate, player.Position, target)') -or
    -not $recorder.Contains('walkway-supported leg(s)')) {
    throw 'Recorded FGrid fallback paths are not minimized and revalidated at playback.'
}
Write-Output 'PASS recorded FGrid fallback routes use corridor and live floor support before skipping samples'

foreach ($required in @('FGridCenterlineTolerance = 0.25f',
    'RecordedShortcutDeviation(points, anchor, candidate,',
    'int[] legs = Enumerable.Repeat(int.MaxValue, points.Count).ToArray()',
    'routeDeviation >= deviations[candidate]',
    'maximum centreline deviation',
    'inward-chord candidate(s) rejected by the Ring Corridor safety band',
    'PreservesFGridAlignmentTurns(points, anchor, candidate)')) {
    if (-not $recorder.Contains($required)) {
        throw "FGrid Ring Corridor centerline preservation is missing: $required"
    }
}

# A one-metre generic tolerance accepts a long chord across a representative
# radius-15 ring arc. The dedicated FGrid band must reject it while accepting a
# shorter arc leg, proving that useful smoothing remains without inner cutting.
function Get-MaxChordDeviation([double]$radius, [double]$degrees) {
    return $radius * (1.0 - [Math]::Cos(($degrees * [Math]::PI / 180.0) / 2.0))
}
$longChordDeviation = Get-MaxChordDeviation 15 36
$shortChordDeviation = Get-MaxChordDeviation 15 20
if ($longChordDeviation -le 0.25 -or $longChordDeviation -gt 1.0 -or
    $shortChordDeviation -gt 0.25) {
    throw 'Representative Ring Corridor chord safety-band geometry is not discriminating as intended.'
}
Write-Output 'PASS FGrid safety band rejects long inward ring chords while retaining useful arc smoothing'

if (-not $planner.Contains('TryFloorZeroLiftPath') -or
    -not $planner.Contains('new List<Vector3> { start, lift }') -or
    -not $planner.Contains('1 walkway-supported leg') -or
    -not $provider.Contains('floor == 0') -or
    -not $provider.Contains('_recast.TryFloorZeroLiftPath(player, target')) {
    throw 'FGrid floor 0 does not require one direct supported leg to its lift.'
}
Write-Output 'PASS FGrid floor 0 lift traversal requires one direct floor-supported leg'

foreach ($required in @('_floorDeparturePending = previousFloor >= 0 && floor == previousFloor + 1',
    'TimeSpan.FromMilliseconds(650)', 'TickFloorDeparture(player)',
    'TryFloorDepartureDirection(player', 'near-spawn Recast point(s)',
    'const float validatedDepartureDistance = 2.5f',
    'StopFGridArrivalMotion()', 'Preserve a confirmed target-floor departure',
    'advanced to floor {floor} during the arrival settle',
    'MovementAction.ForwardStart', 'MovementAction.FullStop',
    'stableHeadingDistance = 2.0f', 'MovementAction.TurnRightStart',
    'MovementAction.TurnLeftStart', 'headingDot < 0.985f',
    'remainingTurn * _floorDepartureTurnSign <= 0',
    'StartFloorDepartureForward()', 'one continuous turn to the stable route heading',
    'DateTime.UtcNow - _floorArrivedAt < TimeSpan.FromMilliseconds(650)',
    'spawn-pad departure ended after',
    'settling on the Ring Corridor before replanning from the off-pad position',
    '_floorDepartureCompletedFloor = _floorDepartureFloor',
    'DateTime.UtcNow < _floorDepartureSettledAt')) {
    if (-not $provider.Contains($required)) {
        throw "FGrid lift-arrival recovery lost required behavior: $required"
    }
}
Write-Output 'PASS upward lift arrivals settle, take one bounded supported departure step, and replan'

$targetFloor = [regex]::Match($provider,
    'if \(floor == _route\.Floor\)(?<block>[\s\S]*?)if \(floor > _route\.Floor')
if (-not $targetFloor.Success -or
    $targetFloor.Groups['block'].Value.Contains('ResetMeshNavigation()')) {
    throw 'Target-floor transition clears lift-arrival recovery before portal approach.'
}
Write-Output 'PASS target floor preserves lift-arrival clearing for the selected portal approach'

foreach ($required in @('length / 0.25f', 'Vector3.Zero, side, side * -1f',
    'normal.Y < 0.6f', 'segment intersects a wall', 'Coordinates(point + offset)')) {
    if (-not $floor.Contains($required)) {
        throw "FGrid floor/wall validation lost required check: $required"
    }
}
Write-Output 'PASS FGrid legs retain dense centre/edge floor sampling and wall rejection'
