$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$accepted = Get-Content -LiteralPath (Join-Path $root 'RKmission/AcceptedMissions.cs') -Raw
$checkpoint = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCheckpoint.cs') -Raw
$resolver = Get-Content -LiteralPath (Join-Path $root 'RKmission/UseItemRoleResolver.cs') -Raw
$handIn = Get-Content -LiteralPath (Join-Path $root 'RKmission/ReturnItemHandIn.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw

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
    'TerminalInstance != 0')) {
    if (-not $checkpoint.Contains($required)) { throw "Identity-level origin ownership regressed: $required" }
}
if ($checkpoint.Contains('TerminalInstance > 0')) {
    throw 'Signed mission-terminal identities are still rejected by a positive-only validity check.'
}

$signedTerminal = [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]3221291548), 0) # 0xC001021C
if ($signedTerminal -ge 0) { throw 'Regression fixture must be a negative signed terminal identity.' }

function Test-OriginIdentityMatch($bindingQuest, $bindingCharacter, $terminal, $quest, $character) {
    return $bindingQuest -gt 0 -and $bindingQuest -eq $quest -and
        $bindingCharacter -gt 0 -and $bindingCharacter -eq $character -and
        $terminal -ne 0
}

if (-not (Test-OriginIdentityMatch 101 17 $signedTerminal 101 17)) {
    throw 'Negative nonzero terminal instance must match exact quest/character ownership.'
}
if (-not (Test-OriginIdentityMatch 101 17 12345 101 17)) {
    throw 'Positive terminal instance must continue to match exact quest/character ownership.'
}
if (Test-OriginIdentityMatch 101 17 0 101 17) {
    throw 'Zero terminal instance must remain invalid.'
}
if (Test-OriginIdentityMatch 101 17 $signedTerminal 102 17) {
    throw 'Wrong quest must not match origin ownership.'
}
if (Test-OriginIdentityMatch 101 17 $signedTerminal 101 18) {
    throw 'Wrong character must not match origin ownership.'
}
Write-Output 'PASS signed negative and positive terminal instances match; zero, wrong quest, and wrong character fail'

foreach ($required in @(
    'TryMatch(mission, character, out _)',
    'public bool TryMatch(AcceptedMission mission, Identity character, out string diagnostics)',
    'QuestInstance == mission.Id.Instance',
    'CharacterInstance > 0 && CharacterInstance == character.Instance',
    'DestinationPlayfield == mission.PlayfieldId',
    'AcceptedMissions.Finite(Destination)',
    'AcceptedMissions.Finite(mission.Entrance)',
    'Vector3.Distance(Destination, mission.Entrance)',
    'entranceDistance <= 12f',
    'TerminalInstance != 0',
    'TerminalPlayfield > 0',
    'AcceptedMissions.Finite(TerminalPosition)')) {
    if (-not $checkpoint.Contains($required)) { throw "Origin provenance weakened: $required" }
}

function Test-FullOriginMatch($terminal, $questMatches, $characterMatches, $playfieldMatches,
    $destinationFinite, $entranceFinite, $entranceDistance, $terminalPlayfield, $terminalPositionFinite) {
    return $questMatches -and $characterMatches -and $playfieldMatches -and
        $destinationFinite -and $entranceFinite -and $entranceDistance -le 12 -and
        $terminal -ne 0 -and $terminalPlayfield -gt 0 -and $terminalPositionFinite
}

if (-not (Test-FullOriginMatch $signedTerminal $true $true $true $true $true 12 540 $true)) {
    throw 'Full provenance must accept a negative nonzero terminal instance when every strict check passes.'
}
if (Test-FullOriginMatch 0 $true $true $true $true $true 12 540 $true) {
    throw 'Full provenance must reject a zero terminal instance.'
}
$wrongPlayfield = Test-FullOriginMatch $signedTerminal $true $true $false $true $true 12 540 $true
$outsideEntranceTolerance = Test-FullOriginMatch $signedTerminal $true $true $true $true $true 12.01 540 $true
$invalidTerminalPosition = Test-FullOriginMatch $signedTerminal $true $true $true $true $true 12 540 $false
if ($wrongPlayfield -or $outsideEntranceTolerance -or $invalidTerminalPosition) {
    throw 'Full provenance must retain playfield, entrance-tolerance, and terminal-position checks.'
}
Write-Output 'PASS full match accepts signed terminals while retaining quest, character, playfield, entrance, and terminal-position provenance'

foreach ($required in @(
    'quest stored=',
    'character stored=',
    'destination playfield stored=',
    'destination finite=',
    'entrance finite=',
    'entrance distance=',
    'within 12m=',
    'terminal instance=',
    'nonzero=',
    'terminal playfield=',
    'terminal position finite=')) {
    if (-not $checkpoint.Contains($required)) { throw "Full provenance diagnostic regressed: $required" }
}
Write-Output 'PASS full provenance failure reports every strict comparison and stored/current identity values'

foreach ($required in @(
    'BindAcceptedMissionOrigin(acceptedId, destination, window.PendingAutoLocation);',
    'ReconcileAcceptedMissionOrigin(accepted);',
    'private bool ReconcileAcceptedMissionOrigin(AcceptedMission accepted)',
    'x.QuestInstance == accepted.Id.Instance',
    'x.CharacterInstance == DynelManager.LocalPlayer.Identity.Instance',
    'x.TerminalInstance != 0',
    'origin.DestinationPlayfield != accepted.PlayfieldId',
    'origin.Destination = accepted.Entrance;',
    '_checkpoint.Save(true, Say);',
    'live entrance adjustment=',
    'issuing terminal {origin.TerminalIdentity} at PF {origin.TerminalPlayfield} preserved')) {
    if (-not $bot.Contains($required)) { throw "Accepted entrance reconciliation regressed: $required" }
}
$refresh = $bot.IndexOf('_missions.Refresh(); // Track acceptance/removal even while local automation is disarmed.')
$lateReconcile = $bot.IndexOf('ReconcileAcceptedMissionOrigin(resolved);', $refresh)
$detailsRemoval = $bot.IndexOf('_awaitingQuestDetails.Remove(id);', $lateReconcile)
if ($refresh -lt 0 -or $lateReconcile -le $refresh -or $detailsRemoval -le $lateReconcile) {
    throw 'Late quest details no longer reconcile the accepted entrance before release.'
}
Write-Output 'PASS exact accepted quest replaces the offer anchor immediately or when delayed details resolve'

$continuousLoop = $bot.IndexOf('foreach (AcceptedMission accepted in _missions.Records.Where(x =>')
$continuousCall = $bot.IndexOf('ReconcileAcceptedMissionOrigin(accepted);', $continuousLoop)
$disarmedReturn = $bot.IndexOf('if (!_running) return;', $continuousCall)
if ($continuousLoop -le $refresh -or $continuousCall -le $continuousLoop -or
    $disarmedReturn -le $continuousCall -or
    -not $bot.Contains('x.Present && x.IsRubiKaDestination')) {
    throw 'Live accepted entrances are not continuously reconciled before the disarmed return/hand-in path.'
}

$storedEntrance = [double[]](100, 5, 100)
$authoritativeEntrance = [double[]](140, 5, 122)
$adjustment = [Math]::Sqrt(
    [Math]::Pow($storedEntrance[0] - $authoritativeEntrance[0], 2) +
    [Math]::Pow($storedEntrance[1] - $authoritativeEntrance[1], 2) +
    [Math]::Pow($storedEntrance[2] - $authoritativeEntrance[2], 2))
if ($adjustment -le 12) { throw 'Entrance-update fixture must exceed the provenance tolerance.' }
$storedEntrance = $authoritativeEntrance
$afterReconcile = [Math]::Sqrt(
    [Math]::Pow($storedEntrance[0] - $authoritativeEntrance[0], 2) +
    [Math]::Pow($storedEntrance[1] - $authoritativeEntrance[1], 2) +
    [Math]::Pow($storedEntrance[2] - $authoritativeEntrance[2], 2))
if ($afterReconcile -gt 0.01) { throw 'Authoritative entrance reconciliation did not eliminate a >12m stale anchor.' }
Write-Output 'PASS a >12m live entrance update is reconciled before strict hand-in provenance validation'

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

$handInPath = $bot
$handInStart = $handInPath.IndexOf('private bool TickReturnHandIn')
$handInEnd = $handInPath.IndexOf('private ', $handInStart + 1)
$handInTick = $handInPath.Substring($handInStart, $handInEnd - $handInStart)
if (-not $handInTick.Contains('x.Matches(_selected, DynelManager.LocalPlayer.Identity)') -or
    -not $handInTick.Contains('candidate.TryMatch(_selected, DynelManager.LocalPlayer.Identity') -or
    -not $handInTick.Contains('x.QuestInstance == _selected.Id.Instance') -or
    $handInTick.Contains('MatchesQuestCharacter')) {
    throw 'Actual ReturnItem hand-in no longer requires full origin provenance.'
}
Write-Output 'PASS actual ReturnItem hand-in still uses full provenance and diagnoses the exact quest candidate'

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
