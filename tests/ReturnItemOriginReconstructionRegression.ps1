$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$accepted = Get-Content -LiteralPath (Join-Path $root 'RKmission/AcceptedMissions.cs') -Raw
$checkpoint = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCheckpoint.cs') -Raw
$resolver = Get-Content -LiteralPath (Join-Path $root 'RKmission/UseItemRoleResolver.cs') -Raw
$handIn = Get-Content -LiteralPath (Join-Path $root 'RKmission/ReturnItemHandIn.cs') -Raw

foreach ($required in @(
    'public Identity VerifiedIssuingTerminal = Identity.None;',
    'RestoreReturnItemFromOrigin(record);',
    'x.Matches(mission, character)',
    'action.Destination == x.TerminalIdentity',
    'mission.Kind != RkMissionKind.Unknown && mission.Kind != RkMissionKind.ReturnItem',
    'mission.Kind != RkMissionKind.Unknown',
    'mission.Kind = RkMissionKind.ReturnItem;',
    'mission.Source = terminal;',
    'PersistObjectiveMetadata(mission);',
    'Restored ReturnItem semantics for quest')) {
    if (-not $accepted.Contains($required)) { throw "Origin reconstruction regressed: $required" }
}
Write-Output 'PASS unknown kind is restored only from an exact matched origin/action terminal'
Write-Output 'PASS restored ReturnItem kind is persisted for retained mission reconstruction'

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
    -not $restore.Contains('action.Destination == x.TerminalIdentity')) {
    throw 'ReturnItem restoration guesses by terminal type instead of exact bound identity.'
}
Write-Output 'PASS unmatched or merely MissionTerminal-typed destinations are not guessed as ReturnItem'

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
