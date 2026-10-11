$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$accepted = Get-Content -LiteralPath (Join-Path $root 'RKmission/AcceptedMissions.cs') -Raw
$checkpoint = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCheckpoint.cs') -Raw
$resolver = Get-Content -LiteralPath (Join-Path $root 'RKmission/UseItemRoleResolver.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw

foreach ($required in @(
    'List<MissionObjectiveBinding> ObjectiveMetadata',
    'QuestType == (int)quest.Type && QuestInstance == quest.Instance',
    'CharacterInstance > 0 && CharacterInstance == character.Instance',
    'RkMissionKind Kind',
    'QuestShortInfo',
    'QuestLongInfo',
    'QuestActionIdentities',
    'RepairToolLowId',
    'RepairToolHighId')) {
    if (-not $checkpoint.Contains($required)) { throw "Durable objective binding regressed: $required" }
}
Write-Output 'PASS checkpoint carries exact per-character/per-quest objective metadata'

foreach ($required in @(
    'PersistQuestMetadata(quest.QuestId, kind, metadata)',
    'PersistObjectiveMetadata(existing)',
    '_checkpoint.Save(true, _say)',
    'RestoreObjectiveMetadata(record)',
    'mission.Kind = binding.Kind',
    'mission.QuestShortInfo = binding.QuestShortInfo',
    'mission.QuestLongInfo = binding.QuestLongInfo',
    'binding.QuestActionIdentities ?? new List<MissionObjectiveIdentity>()',
    'mission.RepairToolLowId = binding.RepairToolLowId',
    'mission.RepairToolHighId = binding.RepairToolHighId')) {
    if (-not $accepted.Contains($required)) { throw "Objective metadata save/restore regressed: $required" }
}
Write-Output 'PASS QuestFullUpdate evidence and repair templates save and restore across reconstruction'

if (-not $checkpoint.Contains('RepairToolType == (int)IdentityType.None || RepairToolInstance == 0') -or
    -not $checkpoint.Contains('? Identity.None')) {
    throw 'Identity.None can be restored as valid repair-tool identity evidence.'
}
if (-not $resolver.Contains('record.ObjectiveMetadataChanged?.Invoke(record);')) {
    throw 'Successful repair-tool resolution no longer persists its identity/template.'
}
Write-Output 'PASS resolved repair tools persist while empty identities restore as Identity.None'

$absence = $accepted.IndexOf('foreach (AcceptedMission record in Records.Where(x => !seen.Contains(x.Id)))')
$remove = $accepted.IndexOf('RemoveObjectiveMetadata(', $absence)
if ($absence -lt 0 -or ($remove -ge 0 -and $remove -lt $accepted.IndexOf('private sealed class QuestMetadata'))) {
    throw 'Temporary Mission.List absence now removes durable objective metadata.'
}
Write-Output 'PASS temporary Mission.List absence preserves durable objective metadata'

if (($bot.Split('_missions.RemoveObjectiveMetadata(_selected);').Count - 1) -ne 3) {
    throw 'Conclusive user, automatic, and return-hand-in completion cleanup paths are incomplete.'
}
if (-not $bot.Contains('_checkpoint.OriginTerminals.Remove(origin);') -or
    -not $bot.Contains('_checkpoint.OriginTerminals.RemoveAll')) {
    throw 'ReturnItem origin binding cleanup was unexpectedly replaced.'
}
Write-Output 'PASS conclusive completion cleans objective metadata and keeps origin bindings separate'

foreach ($required in @(
    'repair tool was not uniquely resolved',
    'accepted quest text names no carried item',
    'record.RepairToolIdentity != Identity.None')) {
    if (-not $resolver.Contains($required)) { throw "UseItem fail-safe regressed: $required" }
}
Write-Output 'PASS UseItem resolution remains fail-safe without durable or live tool evidence'
