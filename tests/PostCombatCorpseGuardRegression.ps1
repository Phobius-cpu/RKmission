param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

foreach ($required in @(
    'PostCombatCorpseMaterializationWait = TimeSpan.FromSeconds(5)',
    'RememberCombatEngagement(room, enemy)',
    'ObservePostCombatCorpseGuard()',
    'HoldForPostCombatCorpse(room)',
    'PostCombatCorpseSettlementWait = TimeSpan.FromSeconds(90)',
    '_loot.RegisterOutstandingMissionLoot(corpse.Identity, guard.RoomId)',
    '_loot.IsMissionLootSettled(guard.Corpse)',
    'PostCombatCorpseGuardActive(liveRoom.Instance)',
    '!guard.ExistingCorpses.Contains(x.Identity)',
    'x.Identity.Instance == guard.Enemy.Instance',
    'Vector3.Distance(x.Position, guard.DeathPosition) <= 6f')) {
    if (-not $dungeon.Contains($required)) {
        throw "Post-combat corpse materialization guard regressed: $required"
    }
}
Write-Output 'PASS RKMission-controlled kills create a bounded same-room corpse materialization guard'

$observe = $dungeon.IndexOf('ObservePostCombatCorpseGuard();')
$readiness = $dungeon.IndexOf('if (_transition == null && HoldForReadiness()) return;')
$fight = $dungeon.IndexOf('bool roomAction = FightInRoom(room)')
$guard = $dungeon.IndexOf('if (HoldForPostCombatCorpse(room)) return;', $fight)
$clear = $dungeon.IndexOf('_clearedRooms.Add(room.Instance);', $guard)
if ($observe -lt 0 -or $readiness -lt 0 -or $observe -gt $readiness -or
    $fight -lt 0 -or $guard -lt $fight -or $clear -lt $guard) {
    throw 'Corpse guard no longer survives readiness or precedes room clearance/routing.'
}
Write-Output 'PASS corpse rescan is observed before readiness and blocks clearance/routing after combat work'

if (-not $dungeon.Contains('_engagementExistingCorpses.Clear()') -or
    -not $dungeon.Contains('armed.ExistingCorpses.UnionWith(_engagementExistingCorpses)') -or
    -not $dungeon.Contains('_postCombatCorpseGuards.Add(armed)') -or
    -not $dungeon.Contains('!_postCombatCorpseGuards.Any(other => other.Corpse == x.Identity)') -or
    -not $dungeon.Contains('registered as outstanding ManagerLoot work') -or
    -not $dungeon.Contains('Post-combat corpse guard settled')) {
    throw 'Corpse matching no longer excludes pre-existing/settled sources or remains room-scoped.'
}
Write-Output 'PASS pre-existing corpses are excluded and matched identity remains owned through ManagerLoot settlement'

if (-not $dungeon.Contains('Post-combat corpse guard timed out') -or
    -not $dungeon.Contains('_roomQuietAt = DateTime.MinValue') -or
    -not $dungeon.Contains('Post-combat corpse guard rescanning room') -or
    -not $dungeon.Contains('Post-combat corpse guard safe-stop') -or
    -not $dungeon.Contains('visible={DynelManager.GetDynel(guard.Corpse) != null}')) {
    throw 'Corpse wait is not bounded or lacks useful diagnostics.'
}
Write-Output 'PASS materialization and settlement waits are bounded, tolerate transient absence, and safe-stop diagnostically'
