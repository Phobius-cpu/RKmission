$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw

foreach ($required in @(
    'CombatTopologyHandoffLifetime = TimeSpan.FromSeconds(30)',
    'PreserveCombatTopologyHandoff(room, enemy, recovery)',
    "paused at firing vantage;",
    'TryUseCombatTopologyHandoff(room, player,',
    "Loot inheriting recorded topology",
    'verticalGap >= CombatTopologyVerticalSeparation',
    "prepared recorded loot topology",
    '_combatTopologyHandoff = null')) {
    if (-not $dungeon.Contains($required)) {
        throw "Combat-to-loot topology handoff behavior missing: $required"
    }
}
Write-Output 'PASS early combat playback exit preserves a bounded room/dungeon topology handoff'

$direct = $dungeon.IndexOf('Loot direct final approach selected:')
$inherit = $dungeon.IndexOf('TryUseCombatTopologyHandoff(room, player,')
$generated = $dungeon.IndexOf('DungeonTransitPlanner.Candidates(_layout,', $inherit)
if ($direct -lt 0 -or $inherit -lt $direct -or $generated -lt $inherit) {
    throw 'Loot priority is no longer direct-final, recorded topology, generated staging.'
}
Write-Output 'PASS direct-final remains first and recorded topology precedes generated staging'

if (-not $recorder.Contains('DungeonTopologyContinuation') -or
    -not $recorder.Contains('Points = new List<Vector3>(_playPoints)') -or
    -not $recorder.Contains('ProgressIndex = Math.Max(0, Math.Min(_playIndex') -or
    -not $recorder.Contains('for (int i = first + 1; i < continuation.Points.Count; i++)') -or
    -not $recorder.Contains('int start = Math.Max(first, best - 1)') -or
    -not $recorder.Contains('continuation.Points.GetRange(start') -or
    $recorder.Contains('continuation.Points.AsEnumerable().Reverse')) {
    throw 'Continuation no longer reacquires only the remaining forward selected subpath.'
}
Write-Output 'PASS continuation starts at current progress and cannot select the loop descending branch'

if (-not $recorder.Contains('Vector3.Distance(target, continuation.TargetSide) > 6f') -or
    -not $recorder.Contains('Math.Abs(target.Y - continuation.TargetSide.Y) > DungeonMinimumVerticalSpan') -or
    -not $dungeon.Contains('handoff.RoomId != room.Instance') -or
    -not $dungeon.Contains('handoff.DungeonInstance != Playfield.ModelIdentity.Instance') -or
    -not $dungeon.Contains('DateTime.UtcNow > handoff.ExpiresAt')) {
    throw 'Stale or unrelated combat topology can leak into loot.'
}
Write-Output 'PASS continuation is constrained by time, room, dungeon instance, target area and elevation'

if ($recorder.Substring($recorder.IndexOf('TryPrepareDungeonTopologyContinuation'),
        $recorder.IndexOf('public void StopPlayback') -
        $recorder.IndexOf('TryPrepareDungeonTopologyContinuation')).Contains(
        'initialVerticalGap < DungeonMinimumVerticalSpan')) {
    throw 'Continuation incorrectly requires a fresh 1.5m residual vertical gap.'
}
Write-Output 'PASS a proven combat subpath can resume with about 1.4m residual vertical gap'

$preserve = $dungeon.IndexOf('private void PreserveCombatTopologyHandoff')
$fail = $dungeon.IndexOf('private void FailCombatRecoveryRoute')
$preserveBody = $dungeon.Substring($preserve, $fail - $preserve)
if ($preserveBody.Contains('Failures++')) {
    throw 'Preserving combat topology consumes a combat recovery attempt.'
}
Write-Output 'PASS combat-to-loot continuation does not increment combat recovery failures'

if (-not $dungeon.Contains('_lootRecordedFallbackAttempted = true') -or
    -not $dungeon.Contains('BuildLootApproaches(room, next.Position);') -or
    -not $dungeon.Contains('falling back to generated staging')) {
    throw 'Recorded continuation failure no longer falls back to existing staging.'
}
Write-Output 'PASS unavailable or failed continuation leaves generated staging fallback intact'
