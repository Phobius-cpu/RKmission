param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$transit = Get-Content -LiteralPath (Join-Path $root 'RKmission/DungeonTransitPlanner.cs') -Raw
$approach = Get-Content -LiteralPath (Join-Path $root 'RKmission/DungeonApproachPlanner.cs') -Raw
$loot = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw

if (-not $transit.Contains('TryDungeonGroundCost') -or
    $transit.Contains('TryDungeonCombatCost') -or
    -not $transit.Contains('layout.IsInside(roomId, from') -or
    -not $transit.Contains('layout.IsInside(roomId, destination') -or
    -not $transit.Contains('target.Y, (target.Y + from.Y) * 0.5f, from.Y')) {
    throw 'Shared transit no longer requires complete ordinary same-room routes with target-level candidates.'
}
Write-Output 'PASS shared transit uses complete ordinary dungeon routes across target/current elevations'

if (-not $transit.Contains('TryDungeonGroundPath') -or
    -not $transit.Contains('CorridorDerived') -or
    -not $transit.Contains('VerticalProgress') -or
    -not $transit.Contains('hasProgressive') -or
    -not $dungeon.Contains('_lootBestVerticalGap') -or
    -not $dungeon.Contains('insufficient vertical progress') -or
    -not $dungeon.Contains('no complete ramp-connected route made meaningful elevation progress')) {
    throw 'Multi-level corridor discovery, monotonic elevation guard, or diagnostics regressed.'
}
Write-Output 'PASS multi-level loot discovers navmesh corridor elevations and rejects recycled same-level staging'

if (-not $dungeon.Contains('DungeonTransitPlanner.Candidates(_layout') -or
    -not $dungeon.Contains('Combat local firing approach unavailable') -or
    -not $dungeon.Contains('Combat same-room transit completed') -or
    -not $dungeon.Contains('MaxCombatTopologyAttempts = 3') -or
    -not $dungeon.Contains('Vector3.Distance(position, recovery.Anchor) >= 12f') -or
    $dungeon.Contains('Vector3.Distance(position, recovery.Anchor) >= 3f ||')) {
    throw 'Combat transit fallback or bounded topology recovery regressed.'
}
Write-Output 'PASS combat uses shared transit and bounds retries without 3m reset loops'

if (-not $dungeon.Contains('MovementOwner activeOwner = MovementArbiter.Current.Owner;') -or
    -not $dungeon.Contains('activeOwner == MovementOwner.CombatPosition ||') -or
    -not $dungeon.Contains('(activeOwner == MovementOwner.None && _requestedOwner == MovementOwner.CombatPosition)') -or
    -not $dungeon.Contains('if (destinationIsCombatOwned) _destination = null;')) {
    throw 'Combat reset no longer preserves destinations owned by loot or other movement work.'
}
Write-Output 'PASS combat reset clears combat-owned destinations without invalidating active loot or other owners'

if (-not $dungeon.Contains('private const float LootUseRange = 3f') -or
    -not $dungeon.Contains('_loot.MissionInteractionReady = LootInteractionReady') -or
    -not $dungeon.Contains('DungeonTransitPlanner.Candidates(_layout') -or
    -not $approach.Contains('? new[] { target.Y, from.Y, (target.Y + from.Y) * 0.5f }') -or
    -not $loot.Contains('MissionInteractionReady?.Invoke(c) == true') -or
    -not $loot.Contains('too far away') -or
    -not $loot.Contains('opening ownership released back to approach')) {
    throw 'Loot interaction readiness, elevation candidates, or too-far ownership release regressed.'
}
Write-Output 'PASS loot waits for conservative 3D readiness, includes target elevation, and releases too-far opening'

$directBuild = $dungeon.IndexOf('_lootApproachPoints.AddRange(DungeonApproachPlanner.Candidates(_layout')
$transitBuild = $dungeon.IndexOf('List<DungeonTransitPlanner.Route> transit = DungeonTransitPlanner.Candidates(_layout')
if ($directBuild -lt 0 -or $transitBuild -lt 0 -or $directBuild -gt $transitBuild -or
    -not $dungeon.Contains('Vector3.Distance(point, target) <= LootUseRange') -or
    -not $dungeon.Contains('LocalRoutePlanner.ClearInteractionSegment(point + Vector3.Up') -or
    -not $dungeon.Contains('_lootTransitStaging = _lootApproachPoints.Count == 0;') -or
    -not $dungeon.Contains('Loot direct final approach selected:') -or
    -not $dungeon.Contains('Loot direct final unavailable; {elevation} transit selected') -or
    -not $dungeon.Contains('transit staging reached; rebuilding direct final approaches from new position') -or
    -not $dungeon.Contains('BuildLootApproaches(room, next.Position);')) {
    throw 'Direct-final-first loot selection or post-staging rebuild regressed.'
}
Write-Output 'PASS loot prefers complete interaction-clear final points and rebuilds after transit staging'

if (-not $dungeon.Contains('Vector3.Distance(position, _lootLastPosition) > 0.75f') -or
    -not $dungeon.Contains('Actual movement along the nav route is progress.') -or
    -not $dungeon.Contains('now - _lootLastProgress > TimeSpan.FromSeconds(10)') -or
    -not $dungeon.Contains('retained approach ownership;')) {
    throw 'Loot route progress retention or topology detour allowance regressed.'
}
Write-Output 'PASS active loot routes retain ownership while movement progresses, including detours'

if (-not $dungeon.Contains('_requestedOwner == MovementOwner.LootApproach') -or
    -not $dungeon.Contains('_destination.HasValue &&') -or
    -not $dungeon.Contains('_lootApproachIndex < _lootApproachPoints.Count') -or
    -not $dungeon.Contains('int rejectedRoute = _lootApproachIndex + 1;') -or
    -not $dungeon.Contains('_lootApproachIndex++;') -or
    -not $dungeon.Contains('MovementArbiter.Current.Halt(MovementOwner.LootApproach);') -or
    -not $dungeon.Contains('rejecting route and trying') -or
    -not $dungeon.Contains('rejecting final route') -or
    -not $dungeon.Contains('if (tier >= 2)') -or
    -not $dungeon.Contains('MovementArbiter.Current.Halt(_requestedOwner);') -or
    -not $dungeon.Contains('InvalidatePath();')) {
    throw 'Loot-specific route advancement or generic non-loot stuck recovery regressed.'
}
Write-Output 'PASS loot stuck recovery advances candidates one by one while generic recovery remains intact'

if (-not $loot.Contains('CurrentProcess = ProcessState.PickingLock;') -or
    -not $loot.Contains('lockPick?.UseOn(chest);') -or
    -not $loot.Contains('IsMissionCriticalLoot') -or
    -not $dungeon.Contains('Mission-critical loot')) {
    throw 'ManagerLoot lockpick or mission-critical protection paths were removed.'
}
Write-Output 'PASS lockpick and mission-critical loot safety paths remain present'
