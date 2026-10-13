param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manager = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert ($manager.Contains('(previousRoom != roomId || newObservedOccurrence)') -and
    $manager.Contains('_missionLootOccurrences[identity] = ++_nextMissionLootOccurrence') -and
    $manager.Contains('_openedMissionLootOccurrences.Remove(identity)') -and
    $manager.Contains('_finishedMissionLootOccurrences.Remove(identity)') -and
    $manager.Contains('openedContainers.Remove(identity.Instance)') -and
    $manager.Contains('stale open/finish evidence invalidated')) `
    'A reused identity does not start clean occurrence-local processing state.'
Write-Output 'PASS cross-room and explicit same-room identity reuse invalidate stale completion'

Assert ($manager.Contains('else if (!_missionLootOccurrences.ContainsKey(identity))') -and
    $manager.Contains('private void TrackMissionLoot(MissionIdentity identity, int roomId, bool newObservedOccurrence = false)')) `
    'Routine rescans no longer preserve the current occurrence generation.'
Write-Output 'PASS repeated scans of one occurrence are idempotent'

Assert ($manager.Contains('_pendingMissionLootOccurrence = _missionLootOccurrences[dynel.Identity]') -and
    $manager.Contains('currentOccurrence != _pendingMissionLootOccurrence') -and
    $manager.Contains('occurrenceRoom != MissionRoomId') -and
    $manager.Contains('_openedMissionLootOccurrences[container.Identity] = _pendingMissionLootOccurrence')) `
    'Late ContainerOpened callbacks are not constrained to the active room occurrence.'
Write-Output 'PASS late open callbacks require the active pending occurrence and room'

Assert ($manager.Contains('Occurrence = _pendingMissionLootOccurrence') -and
    $manager.Contains('_finishedMissionLootOccurrences[CorpseContainer.Identity] = occurrence') -and
    $manager.Contains('openedOccurrence != occurrence) return;')) `
    'Processing completion is not bound to the occurrence that supplied open evidence.'
Write-Output 'PASS completion evidence stays with its source occurrence'

Assert ($dungeon.Contains('public long CorpseOccurrence;') -and
    $dungeon.Contains('long occurrence = _loot.RegisterOutstandingMissionLoot') -and
    $dungeon.Contains('guard.CorpseOccurrence = occurrence') -and
    $dungeon.Contains('guard.Corpse, guard.RoomId, guard.CorpseOccurrence)')) `
    'Post-combat guards can still release from another occurrence of the same identity.'
Write-Output 'PASS guard release requires current-occurrence completion'

Assert ($manager.Contains('foreach (Dynel candidate in candidates) TrackMissionLoot(candidate.Identity, roomId);') -and
    $manager.Contains('!_finishedMissionLoot.Contains(x.Identity)')) `
    'Multiple independently tracked corpse candidates or live-source filtering regressed.'
Write-Output 'PASS two corpses remain independently selectable while stale reused identities are reopened'
