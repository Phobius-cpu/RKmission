param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$loot = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw

if (-not $dungeon.Contains('private readonly List<Vector3> _lootVisitedStaging') -or
    -not $dungeon.Contains('LootStagingRevisitTolerance = 2f') -or
    -not $dungeon.Contains('_lootVisitedStaging.Add(position)') -or
    -not $dungeon.Contains('Vector3.Distance(visited, route.Destination) <= LootStagingRevisitTolerance')) {
    throw 'Loot staging history is not retained and filtered with a positional tolerance.'
}
Write-Output 'PASS recent staging points are remembered per attempt and excluded by tolerance'

if (-not $dungeon.Contains('distance <= _lootBestDistance - 1f') -or
    -not $dungeon.Contains('verticalGap <= _lootBestVerticalGap - 0.75f') -or
    -not $dungeon.Contains('_lootNonImprovingHops++') -or
    -not $dungeon.Contains('staging cycle/non-improving hop detected') -or
    -not $dungeon.Contains('if (!_lootCycleReported)')) {
    throw 'Loot staging does not enforce meaningful progress or single-report cycle detection.'
}
Write-Output 'PASS A-B/non-improving staging is detected once instead of resetting forever'

if (-not $dungeon.Contains('BuildLootApproaches(room, next.Position);') -or
    -not $dungeon.Contains('excluding {_lootVisitedStaging.Count} recent staging point(s)') -or
    -not $dungeon.Contains('staging cycle/non-improving candidates exhausted')) {
    throw 'Cyclic staging does not rebuild while excluding prior points and exhaust deterministically.'
}
Write-Output 'PASS cyclic staging advances to unvisited candidates and exhausts deterministically'

if (-not $loot.Contains('DeferredApproach') -or
    -not $loot.Contains('RetryAfterTraversal = _missionRoomTraversals + 1') -or
    -not $loot.Contains('DeferredApproachReady(x.Identity)') -or
    -not $dungeon.Contains('will retry after room traversal') -or
    -not $dungeon.Contains('DeferredMissionLootWaitingForTraversal') -or
    -not $dungeon.Contains('traversing room {contextRoom.Instance}')) {
    throw 'First ordinary approach failure is not deferred until a room traversal.'
}
Write-Output 'PASS first ordinary exhaustion is deferred and becomes retryable after traversal'

if (-not $loot.Contains('MaxOrdinaryApproachAttempts = 2') -or
    -not $loot.Contains('attempt = ++deferred.Attempts') -or
    -not $loot.Contains('if (attempt >= MaxOrdinaryApproachAttempts)') -or
    -not $loot.Contains('SkipUnreachableMissionLoot(identity)') -or
    -not $dungeon.Contains('Independent retry policy exhausted')) {
    throw 'Ordinary deferred attempts are not bounded before terminal skip.'
}
Write-Output 'PASS two independent ordinary attempts bound retries before terminal skip'

if (-not $dungeon.Contains('if (_loot.IsMissionCriticalLoot(loot.Identity))') -or
    -not $dungeon.Contains('Mission-critical loot') -or
    -not $dungeon.Contains('Stop();') -or
    -not $loot.Contains('MissionSourceState.CriticalBlocked') -or
    -not $loot.Contains('IsMissionCriticalLoot(identity) ||') -or
    -not $loot.Contains('_deferredApproaches.Remove(identity)')) {
    throw 'Mission-critical loot no longer fails closed.'
}
Write-Output 'PASS mission-critical loot remains fail-closed and cannot be silently waived'

if (-not $loot.Contains('state == MissionSourceState.SkippedUnreachable') -or
    $loot.Contains('state == MissionSourceState.DeferredApproach || state == MissionSourceState.SkippedUnreachable')) {
    throw 'Deferred and terminal ordinary loot settlement semantics are no longer distinct.'
}
Write-Output 'PASS only terminally skipped ordinary loot is settled/non-blocking'
