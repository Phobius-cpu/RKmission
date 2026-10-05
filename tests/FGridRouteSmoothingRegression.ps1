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

if (-not $recorder.Contains('candidate = SimplifySupportedFGridPath(candidate, player.Position.Y)') -or
    -not $recorder.Contains('SupportedRecordedFGridRoute(candidate, player.Position, target)') -or
    -not $recorder.Contains('walkway-supported leg(s)')) {
    throw 'Recorded FGrid fallback paths are not minimized and revalidated at playback.'
}
Write-Output 'PASS recorded FGrid fallback routes use corridor and live floor support before skipping samples'

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
    'MovementAction.ForwardStart', 'MovementAction.FullStop',
    'DateTime.UtcNow - _floorArrivedAt < TimeSpan.FromMilliseconds(650)',
    'spawn-pad departure ended after')) {
    if (-not $provider.Contains($required)) {
        throw "FGrid lift-arrival recovery lost required behavior: $required"
    }
}
Write-Output 'PASS upward lift arrivals settle, take one bounded supported departure step, and replan'

foreach ($required in @('length / 0.25f', 'Vector3.Zero, side, side * -1f',
    'normal.Y < 0.6f', 'segment intersects a wall', 'Coordinates(point + offset)')) {
    if (-not $floor.Contains($required)) {
        throw "FGrid floor/wall validation lost required check: $required"
    }
}
Write-Output 'PASS FGrid legs retain dense centre/edge floor sampling and wall rejection'
