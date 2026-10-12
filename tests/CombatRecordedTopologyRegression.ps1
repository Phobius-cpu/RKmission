$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$transit = Get-Content -LiteralPath (Join-Path $root 'RKmission/DungeonTransitPlanner.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw

if (-not $transit.Contains('vertical >= 1.25f && horizontal < 0.75f') -or
    -not $transit.Contains('IsPhysicallyProgressive') -or
    -not $dungeon.Contains('Combat generated same-room route rejected: vertical-only leg')) {
    throw 'Vertical-only generated combat legs are no longer rejected.'
}
Write-Output 'PASS vertical-only generated legs are rejected as non-walkable'

$findGenerated = $dungeon.IndexOf('FindCombatTransitRoute(room, enemy, position)')
$recorded = $dungeon.IndexOf('bool preferRecorded = attemptAvailable', $findGenerated)
if ($findGenerated -lt 0 -or $recorded -lt $findGenerated -or
    -not $dungeon.Contains('!enemy.IsInLineOfSight') -or
    -not $dungeon.Contains('Math.Abs(enemy.Position.Y - position.Y) >= CombatTopologyVerticalSeparation') -or
    -not $dungeon.Contains("matching recorded topology '{recovery.RecordedRoute}' preferred over generated same-room route") -or
    -not $dungeon.Contains('DungeonTransitPlanner.IsPhysicallyProgressive(position, candidate.Waypoints)')) {
    throw 'Blocked multi-level combat no longer prefers matching recorded ground truth over generated transit.'
}
Write-Output 'PASS matching recorded topology wins over generated transit for blocked multi-level combat'

$startRecorded = $dungeon.IndexOf('private bool TryStartRecordedCombatTopology')
$routeCost = $dungeon.IndexOf('private static float CombatRecoveryRouteCost', $startRecorded)
$startBody = $dungeon.Substring($startRecorded, $routeCost - $startRecorded)
if (-not $startBody.Contains('MovementArbiter.Current.Halt(MovementOwner.CombatPosition)') -or
    -not $startBody.Contains('recovery.Waypoints.Clear()') -or
    -not $startBody.Contains('recovery.WaypointIndex = 0') -or
    -not $startBody.Contains('recovery.LastPosition = position') -or
    -not $startBody.Contains('_destination = null')) {
    throw 'Recorded takeover no longer clears generated movement and starts from current player context.'
}
Write-Output 'PASS recorded takeover clears generated waypoint ownership and refreshes current-position progress'

if (-not $dungeon.Contains('_navRoutes.TryPrepareDungeonTopology(_layout') -or
    -not $dungeon.Contains('_navRoutes.TryNavigatePreparedDungeonTopology(enemy.Position') -or
    -not $dungeon.Contains('same-room recorded topology') -or
    -not $dungeon.Contains('using ordered recorded topology')) {
    throw 'Combat no longer prepares and plays generic recorded dungeon topology.'
}
Write-Output 'PASS combat fallback selects and plays generic recorded dungeon topology'

if (-not $recorder.Contains('maximumY - minimumY < 1.5f') -or
    $recorder.Contains('maximumY - minimumY < 3f') -or
    -not $recorder.Contains('startDistance > 8f || targetDistance > 6f') -or
    -not $recorder.Contains('points.Any(point => !layout.IsInside(roomId, point, 0.1f))')) {
    throw 'Recorded topology span, endpoint, or room-containment gates regressed.'
}
Write-Output 'PASS 2.7m recordings qualify while flat/incidental and mismatched recordings remain gated'

if (-not $recorder.Contains('_playIndex++') -or $recorder.Contains('_playIndex--') -or
    -not $recorder.Contains('movement.SetDestination(owner, waypoint)') -or
    -not $recorder.Contains('forward progress')) {
    throw 'Recorded topology playback no longer follows points strictly forward.'
}
Write-Output 'PASS recorded topology follows ordered points forward'

$recordedTick = $dungeon.IndexOf('if (recovery.RecordedTransit)')
$generatedTick = $dungeon.IndexOf('if (recovery.WaypointIndex < recovery.Waypoints.Count)', $recordedTick)
$recordedBody = $dungeon.Substring($recordedTick, $generatedTick - $recordedTick)
if ($recordedBody.Contains('ResetCombatApproach();') -and
    -not $recordedBody.Contains('if (!completed && !betterVerticalVantage && !meaningfulHorizontalProgress)')) {
    throw 'Recorded intermediate waypoints can rebuild local firing candidates.'
}
if (-not $dungeon.Contains("completed; rebuilding firing candidates") -or
    -not $dungeon.Contains('reached a materially better vantage; rebuilding firing candidates')) {
    throw 'Recorded combat playback no longer uses bounded rebuild gates.'
}
Write-Output 'PASS intermediate recorded waypoints do not rebuild firing candidates'

$begin = $dungeon.IndexOf('private void BeginCombatRecovery')
$tick = $dungeon.IndexOf('private bool TickCombatRecovery')
$beginBody = $dungeon.Substring($begin, $tick - $begin)
$tickEnd = $dungeon.IndexOf('private void FailCombatRecoveryRoute', $tick)
$tickBody = $dungeon.Substring($tick, $tickEnd - $tick)
if (-not $beginBody.Contains('recovery.Failures++') -or $tickBody.Contains('recovery.Failures++') -or
    -not $tickBody.Contains('under retry={recovery.Failures}/{MaxCombatTopologyAttempts}')) {
    throw 'Recorded playback can consume attempts per waypoint or on generated fallback.'
}
Write-Output 'PASS one full recorded playback consumes one topology recovery attempt'

if (-not $dungeon.Contains('recorded topology failed/stalled') -or
    -not $dungeon.Contains('recovery.RetryAt = DateTime.UtcNow.AddSeconds(15)') -or
    -not $dungeon.Contains('RoomClearanceState.CombatDeferred') -or
    -not $dungeon.Contains('mission remains armed')) {
    throw 'Recorded failure no longer defers safely with the room unfinished.'
}
Write-Output 'PASS recorded failure remains bounded and leaves the combat room unfinished'

if (-not $dungeon.Contains('same-room transit failed (no complete route)') -or
    -not $dungeon.Contains('MaxCombatTopologyAttempts = 3') -or
    -not $dungeon.Contains('CombatRecoveryWaiting = true')) {
    throw 'No-route bounded recovery behavior regressed.'
}
Write-Output 'PASS no generated or recorded route preserves bounded recovery'
