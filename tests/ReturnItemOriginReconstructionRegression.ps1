$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$accepted = Get-Content -LiteralPath (Join-Path $root 'RKmission/AcceptedMissions.cs') -Raw
$checkpoint = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCheckpoint.cs') -Raw
$resolver = Get-Content -LiteralPath (Join-Path $root 'RKmission/UseItemRoleResolver.cs') -Raw
$handIn = Get-Content -LiteralPath (Join-Path $root 'RKmission/ReturnItemHandIn.cs') -Raw

foreach ($required in @(
    'public Identity VerifiedIssuingTerminal = Identity.None;',
    'RestoreReturnItemFromOrigin(record);',
    'x.MatchesQuestCharacter(mission.Id, character)',
    'action.Destination == x.TerminalIdentity',
    'mission.Kind != RkMissionKind.Unknown && mission.Kind != RkMissionKind.ReturnItem',
    'mission.Kind != RkMissionKind.Unknown',
    'mission.Kind = RkMissionKind.ReturnItem;',
    'mission.Source = terminal;',
    'PersistObjectiveMetadata(mission);',
    'Restored ReturnItem semantics for quest')) {
    if (-not $accepted.Contains($required)) { throw "Origin reconstruction regressed: $required" }
}
Write-Output 'PASS unknown kind is restored from exact quest/character ownership and action terminal identity'
Write-Output 'PASS restored ReturnItem kind is persisted for retained mission reconstruction'

foreach ($required in @(
    'public bool MatchesQuestCharacter(Identity quest, Identity character)',
    'QuestInstance > 0 && QuestInstance == quest.Instance',
    'CharacterInstance > 0 && CharacterInstance == character.Instance',
    'TerminalInstance > 0')) {
    if (-not $checkpoint.Contains($required)) { throw "Identity-level origin ownership regressed: $required" }
}
Write-Output 'PASS wrong quest, wrong character, and invalid terminal bindings cannot restore ReturnItem'

foreach ($required in @(
    'QuestInstance == mission.Id.Instance',
    'CharacterInstance > 0 && CharacterInstance == character.Instance',
    'DestinationPlayfield == mission.PlayfieldId',
    'Vector3.Distance(Destination, mission.Entrance) <= 12f')) {
    if (-not $checkpoint.Contains($required)) { throw "Origin provenance weakened: $required" }
}
Write-Output 'PASS exact quest, character, and destination provenance remains required'

$restoreStart = $accepted.IndexOf('private void RestoreReturnItemFromOrigin')
$restoreEnd = $accepted.IndexOf('private MissionObjectiveBinding FindBinding', $restoreStart)
$restore = $accepted.Substring($restoreStart, $restoreEnd - $restoreStart)
if ($restore.Contains('IdentityType.MissionTerminal') -or
    $restore.Contains('.Matches(mission, character)') -or
    $restore.Contains('mission.PlayfieldId') -or
    $restore.Contains('mission.Entrance') -or
    -not $restore.Contains('action.Destination == x.TerminalIdentity')) {
    throw 'ReturnItem restoration guesses by terminal type or still requires full geometric provenance.'
}
Write-Output 'PASS classification ignores entrance geometry but never guesses unmatched or merely typed terminals'

$handInPath = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$handInStart = $handInPath.IndexOf('private bool TickReturnHandIn')
$handInEnd = $handInPath.IndexOf('private ', $handInStart + 1)
$handInTick = $handInPath.Substring($handInStart, $handInEnd - $handInStart)
if (-not $handInTick.Contains('x.Matches(_selected, DynelManager.LocalPlayer.Identity)') -or
    $handInTick.Contains('MatchesQuestCharacter')) {
    throw 'Actual ReturnItem hand-in no longer requires full origin provenance.'
}
Write-Output 'PASS actual ReturnItem hand-in still uses full geometric provenance'

$returnBranch = $resolver.IndexOf('if (record.Kind == RkMissionKind.ReturnItem')
$repairBranch = $resolver.IndexOf('bool sourceInInventory =')
if ($returnBranch -lt 0 -or $repairBranch -le $returnBranch -or
    -not $resolver.Contains('record.VerifiedIssuingTerminal') -or
    -not $resolver.Contains('record.Kind == RkMissionKind.Unknown') -or
    -not $resolver.Contains('action.Destination != issuingTerminal')) {
    throw 'Verified ReturnItem resolution no longer precedes and bypasses repair inference.'
}
Write-Output 'PASS verified ReturnItem resolution bypasses repair-tool inference'

foreach ($required in @(
    'record.Kind != RkMissionKind.Repair && record.Kind != RkMissionKind.Unknown',
    'TryResolveRepairTool(record',
    'roles.WorldTarget != issuingTerminal',
    'item.UseOn(roles.WorldTarget)')) {
    if (-not ($resolver + $handIn).Contains($required)) { throw "Repair or strict hand-in behavior regressed: $required" }
}
Write-Output 'PASS real Repair routing and exact ReturnItem hand-in checks remain present'
