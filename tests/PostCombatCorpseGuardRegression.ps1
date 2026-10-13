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
    'PostCombatCorpseSkipVisibilityGrace = TimeSpan.FromSeconds(5)',
    'long occurrence = _loot.RegisterOutstandingMissionLoot(corpse.Identity, verifiedRoom)',
    '_loot.IsMissionLootSuccessfullyProcessed(',
    'guard.Corpse, guard.RoomId, guard.CorpseOccurrence)',
    '_loot.TryGetMissionLootSkipReason(guard.Corpse, out _)',
    '_loot.RetryVisibleOutstandingMissionLoot(skipped.Corpse, skipped.RoomId, reason)',
    'PostCombatCorpseGuardActive(liveRoom.Instance)',
    '!guard.ExistingCorpses.Contains(x.Corpse.Identity)',
    'x.Corpse.Identity.Instance == guard.Enemy.Instance',
    'Vector3.Distance(x.Corpse.Position, guard.DeathPosition) <= 6f')) {
    if (-not $dungeon.Contains($required)) {
        throw "Post-combat corpse materialization guard regressed: $required"
    }
}
Write-Output 'PASS RKMission-controlled kills create a bounded verified-room corpse materialization guard'

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

$recovery = $dungeon.IndexOf('private bool TickCombatRecovery')
$recoveryEnd = $dungeon.IndexOf('private void PreserveCombatTopologyHandoff', $recovery)
$recoveryBody = $dungeon.Substring($recovery, $recoveryEnd - $recovery)
$notThreatened = $recoveryBody.IndexOf('if (!playerThreatened)')
$guardBeforeDeparture = $recoveryBody.IndexOf('if (PostCombatCorpseGuardActive(room.Instance))', $notThreatened)
$lootBeforeDeparture = $recoveryBody.IndexOf('if (LootInRoom(room)) return true;', $guardBeforeDeparture)
$holdBeforeDeparture = $recoveryBody.IndexOf('if (HoldForPostCombatCorpse(room)) return true;', $guardBeforeDeparture)
$normalDeparture = $recoveryBody.IndexOf('Room next = NextRoom(room);', $notThreatened)
if ($notThreatened -lt 0 -or $guardBeforeDeparture -lt $notThreatened -or
    $lootBeforeDeparture -lt $guardBeforeDeparture -or $holdBeforeDeparture -lt $lootBeforeDeparture -or
    $normalDeparture -lt $holdBeforeDeparture) {
    throw 'Combat recovery can again depart before guarded same-room corpse processing settles.'
}
Write-Output 'PASS safe combat deferral yields to same-room ManagerLoot and corpse guard before transition'

if (-not $recoveryBody.Contains('if (PostCombatCorpseGuardActive(room.Instance))') -or
    $recoveryBody.Contains('if (!PostCombatCorpseGuardActive(room.Instance))')) {
    throw 'Normal combat deferral without a corpse guard no longer retains its existing transition path.'
}
Write-Output 'PASS normal combat deferral without corpses remains unchanged'

if (-not $dungeon.Contains('_engagementExistingCorpses.Clear()') -or
    -not $dungeon.Contains('armed.ExistingCorpses.UnionWith(_engagementExistingCorpses)') -or
    -not $dungeon.Contains('_postCombatCorpseGuards.Add(armed)') -or
    -not $dungeon.Contains('!_postCombatCorpseGuards.Any(other => other != guard && other.Corpse == x.Corpse.Identity)') -or
    -not $dungeon.Contains('registered as outstanding ManagerLoot work') -or
    -not $dungeon.Contains('Post-combat corpse guard completed')) {
    throw 'Corpse matching no longer excludes pre-existing/settled sources or preserves verified ownership.'
}
Write-Output 'PASS pre-existing corpses are excluded and verified matched identity remains owned through ManagerLoot settlement'

if (-not $dungeon.Contains('Post-combat corpse guard timed out') -or
    -not $dungeon.Contains('_roomQuietAt = DateTime.MinValue') -or
    -not $dungeon.Contains('Post-combat corpse guard rescanning room') -or
    -not $dungeon.Contains('allowing {PostCombatCorpseSkipVisibilityGrace.TotalSeconds:0}s for a transient visibility recovery') -or
    -not $dungeon.Contains('Post-combat corpse guard safe-stop') -or
    -not $dungeon.Contains('visible={DynelManager.GetDynel(guard.Corpse) != null}')) {
    throw 'Corpse wait is not bounded or lacks useful diagnostics.'
}
Write-Output 'PASS materialization and settlement waits are bounded, tolerate transient absence, and safe-stop diagnostically'

$manager = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
foreach ($required in @(
    'private readonly HashSet<MissionIdentity> _openedMissionLoot',
    'private readonly Dictionary<MissionIdentity, long> _missionLootOccurrences',
    '_openedMissionLoot.Add(container.Identity)',
    'public bool IsMissionLootSuccessfullyProcessed(MissionIdentity identity, int roomId, long occurrence)',
    '_openedMissionLoot.Contains(identity) && _finishedMissionLoot.Contains(identity)',
    'state == MissionSourceState.Completed',
    'public bool TryGetMissionLootSkipReason',
    'public void RetryVisibleOutstandingMissionLoot',
    '_deferredApproaches.Remove(identity)',
    'exact ContainerOpened plus processing-finished evidence is still absent')) {
    if (-not $manager.Contains($required)) {
        throw "Exact corpse processing evidence regressed: $required"
    }
}
if ($dungeon.Contains('IsMissionLootSettled(guard.Corpse)')) {
    throw 'Corpse guard again conflates generic skip settlement with successful exact processing.'
}
Write-Output 'PASS guarded corpse success requires exact open/finish evidence and visible skipped corpses are retried'
