$ErrorActionPreference = 'Stop'

$dungeon = Get-Content (Join-Path $PSScriptRoot '..\RKmission\MissionDungeon.cs') -Raw

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Require ($dungeon.Contains('int verifiedRoom = VerifiedDynelRoom(enemy, _engagedEnemyRoom)') -and
    $dungeon.Contains('RememberExistingCorpses(verifiedRoom)')) `
    'Live enemy room changes are no longer tracked with an identity-reuse baseline.'
Write-Output 'PASS live chased enemies update their verified room'

Require ($dungeon.Contains('Playfield.Rooms.SelectMany') -and
    $dungeon.Contains('exact.Count == 1 ? exact[0]') -and
    $dungeon.Contains('exact.Count == 0 && nearby.Count == 1') -and
    $dungeon.Contains('nearby.Count == 1') -and
    $dungeon.Contains('guard.RoomId = verifiedRoom')) `
    'Cross-room corpse matching is missing exact identity, unique proximity, or verified-room ownership.'
Write-Output 'PASS cross-room corpse ownership follows verified containment without ambiguous proximity matches'

Require ($dungeon.Contains('long occurrence = _loot.RegisterOutstandingMissionLoot(corpse.Identity, verifiedRoom)') -and
    $dungeon.Contains('if (occurrence == 0) continue') -and
    $dungeon.Contains('guard.CorpseOccurrence = occurrence') -and
    $dungeon.Contains('IsMissionLootSuccessfullyProcessed(')) `
    'Matched corpses no longer retain occurrence-local ManagerLoot evidence requirements.'
Write-Output 'PASS verified room registration preserves exact occurrence settlement'

Require ($dungeon.Contains('RouteToRoom(wrongRoom.RoomId)') -and
    $dungeon.Contains('bool localOutstanding = room != null') -and
    $dungeon.Contains('PostCombatCorpseGuard wrongRoom = localOutstanding ? null') -and
    $dungeon.Contains('no mapped route from room') -and
    -not $dungeon.Contains('belongs to room {wrongRoom.RoomId}, but current room')) `
    'A remote outstanding corpse still immediately wrong-room stops instead of bounded routing.'
Write-Output 'PASS genuine remote outstanding corpses route back or terminally explain failure'

$transition = $dungeon.Substring($dungeon.IndexOf('private void BeginTransition(int source, int target)'))
Require ($transition.Contains('_postCombatCorpseGuards.FirstOrDefault') -and
    $transition.IndexOf('_postCombatCorpseGuards.FirstOrDefault') -lt $transition.IndexOf('DungeonLayout.Connection edge')) `
    'The central transition path can leave a locally unsettled corpse guard.'
Write-Output 'PASS every room-transition caller is gated by local corpse settlement'

Require ($dungeon.Contains('engagement room was {guard.RoomId}') -and
    $dungeon.Contains('Post-combat corpse recovery routing from room')) `
    'Diagnostics do not explain the 13:38 engagement/corpse-room mismatch and recovery route.'
Write-Output 'PASS FindPerson reserved-aggro cross-room sequence has explicit diagnostics'
