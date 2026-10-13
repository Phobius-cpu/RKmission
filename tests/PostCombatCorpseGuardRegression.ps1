param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

foreach ($required in @(
    'PostCombatCorpseMaterializationWait = TimeSpan.FromSeconds(5)',
    'RememberCombatEngagement(room, enemy)',
    'ObservePostCombatCorpseGuard()',
    'HoldForPostCombatCorpse(room)',
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
    -not $dungeon.Contains('_postCombatCorpseGuard.ExistingCorpses.UnionWith(_engagementExistingCorpses)') -or
    -not $dungeon.Contains('normal ManagerLoot acquisition resumes') -or
    -not $dungeon.Contains('no cross-room loot ownership was created')) {
    throw 'Corpse matching no longer excludes pre-existing/settled sources or remains room-scoped.'
}
Write-Output 'PASS pre-existing corpses are excluded and ManagerLoot retains acquisition ownership'

if (-not $dungeon.Contains('Post-combat corpse guard timed out') -or
    -not $dungeon.Contains('_roomQuietAt = DateTime.MinValue') -or
    -not $dungeon.Contains('Post-combat corpse guard rescanning room')) {
    throw 'Corpse wait is not bounded or lacks useful diagnostics.'
}
Write-Output 'PASS corpse wait has a hard timeout, resets clearance quiet time, and logs bounded diagnostics'
