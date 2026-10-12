$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$transit = Get-Content -LiteralPath (Join-Path $root 'RKmission/DungeonTransitPlanner.cs') -Raw

if (-not $dungeon.Contains('CombatTopologyVerticalSeparation = 1.5f') -or
    -not $transit.Contains('bool separated = verticalGap >= 1.5f;') -or
    -not $dungeon.Contains('candidate.VerticalProgress >= 0.75f')) {
    throw 'A roughly 2.7m combat elevation gap no longer prefers progressive topology routes.'
}
Write-Output 'PASS meaningful sub-3m elevation separation prefers topology-aware combat transit'

if (-not $transit.Contains('public List<Vector3> Waypoints') -or
    -not $transit.Contains('const int maximum = 12;') -or
    -not $transit.Contains('TryDungeonGroundPath(from, destination') -or
    -not $transit.Contains('layout.IsInside(roomId, point, 0.3f)') -or
    -not $dungeon.Contains('recovery.Waypoints.AddRange(route.Waypoints)')) {
    throw 'Combat recovery no longer retains a bounded complete-corridor waypoint route.'
}
Write-Output 'PASS one recovery attempt retains up to 12 ordered complete-corridor waypoints'

$arrival = $dungeon.IndexOf('recovery.WaypointIndex++;')
$continue = $dungeon.IndexOf('if (!betterVerticalVantage && !meaningfulHorizontalProgress && !routeExhausted)')
$reset = $dungeon.IndexOf('ResetCombatApproach();', $arrival)
if ($arrival -lt 0 -or $continue -lt $arrival -or $reset -lt $continue -or
    $dungeon.IndexOf('return true;', $continue) -lt $continue -or
    -not $dungeon.Contains('Combat same-room transit waypoint')) {
    throw 'Intermediate combat transit arrival no longer advances without rebuilding local approaches.'
}
Write-Output 'PASS intermediate waypoint arrival advances the same route without a local firing rebuild'

if (-not $dungeon.Contains('(enemy.IsInLineOfSight && enemy.IsInAttackRange(true))') -or
    -not $dungeon.Contains('betterVerticalVantage') -or
    -not $dungeon.Contains('meaningfulHorizontalProgress') -or
    -not $dungeon.Contains('routeExhausted') -or
    -not $dungeon.Contains('rebuilding normal firing-side candidates')) {
    throw 'Combat transit rebuild gates no longer cover LOS, better vantage, progress, and exhaustion.'
}
Write-Output 'PASS firing candidates rebuild only after an attack opening or justified topology progress'

$begin = $dungeon.IndexOf('private void BeginCombatRecovery')
$tick = $dungeon.IndexOf('private bool TickCombatRecovery')
$beginBody = $dungeon.Substring($begin, $tick - $begin)
$tickEnd = $dungeon.IndexOf('private void FailCombatRecoveryRoute', $tick)
$tickBody = $dungeon.Substring($tick, $tickEnd - $tick)
if (-not $beginBody.Contains('recovery.Failures++') -or
    $tickBody.Contains('recovery.Failures++') -or
    -not $dungeon.Contains('MaxCombatTopologyAttempts = 3')) {
    throw 'Waypoint progress can consume attempts or the three-route cap regressed.'
}
Write-Output 'PASS retries count distinct route builds and remain capped at three'

$fight = $dungeon.IndexOf('private bool FightInRoom')
$beginCall = $dungeon.IndexOf('BeginCombatRecovery(room, enemy);', $fight)
$approachCall = $dungeon.IndexOf('target => ApproachCombatEnemy(target, room.Instance)', $fight)
if ($fight -lt 0 -or $approachCall -lt $fight -or $beginCall -lt $approachCall -or
    -not $dungeon.Contains('bool verticallySeparated = Math.Abs(enemy.Position.Y - position.Y)')) {
    throw 'Ordinary local combat approach no longer precedes topology recovery in flat rooms.'
}
Write-Output 'PASS flat-room combat still uses ordinary local firing approaches first'

if (-not $dungeon.Contains('same-room transit failed (no complete route)') -or
    -not $dungeon.Contains('Room remains unfinished') -or
    -not $dungeon.Contains('mission remains armed') -or
    -not $dungeon.Contains('RoomClearanceState.CombatDeferred') -or
    -not $dungeon.Contains('Handle an immediate different threat first')) {
    throw 'Safe no-route deferral, unfinished-room state, or threat priority regressed.'
}
Write-Output 'PASS no-route recovery defers safely, keeps the room unfinished, and preserves threat priority'
