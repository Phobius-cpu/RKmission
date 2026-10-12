param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$loot = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

foreach ($transactionGuard in @(
    'private sealed class SourceAcquisition',
    'BeginSourceAcquisition(container)',
    'ProcessMissionSourceAcquisition()',
    'ListedItemTransfer selected = _sourceAcquisition.Items[_sourceAcquisition.Current]',
    '_sourceAcquisition.Current++')) {
    if (-not $loot.Contains($transactionGuard)) { throw "Transactional source acquisition regressed: $transactionGuard" }
}
Write-Output 'PASS corpse and container windows use one ordered per-source acquisition transaction'

if (-not $loot.Contains('ManagerLoot selected {_sourceAcquisition.Items.Count}/{container.Items?.Count ?? 0} item(s)') -or
    -not $loot.Contains('Classify(item) == ItemClassification.Keep') -or
    -not $loot.Contains('Rule rule = GetMatchingRule(item)')) {
    throw 'ManagerLoot rules are no longer authoritative when snapshotting selected source items.'
}
Write-Output 'PASS rule-matched selection is snapshotted when either source type opens'

if (-not $loot.Contains('SourceSlot = item.Slot') -or
    -not $loot.Contains('LowId = item.LowId') -or
    -not $loot.Contains('HighId = item.HighId') -or
    -not $loot.Contains('InventoryCountBefore = MatchingInventoryCount(transfer)')) {
    throw 'Selected items lack compound source/inventory evidence for None identities.'
}
Write-Output 'PASS exact identity plus slot/id/QL/name and baseline count support transfer observation'

if (-not $loot.Contains('bool leftSource = !SourceStillContains(selected)') -or
    -not $loot.Contains('bool appearedInInventory = MatchingInventoryCount(selected) > selected.InventoryCountBefore') -or
    -not $loot.Contains('if (leftSource && appearedInInventory)')) {
    throw 'A move can be accepted without both source departure and inventory arrival evidence.'
}
Write-Output 'PASS selected moves require positive source-departure and inventory-arrival evidence'

if (-not $loot.Contains('MaxListedMoveAttempts = 2') -or
    -not $loot.Contains('selected.Attempts >= MaxListedMoveAttempts') -or
    -not $loot.Contains('source remains held') -or
    -not $loot.Contains('safely skipping after {selected.Attempts} bounded attempt(s)')) {
    throw 'Ambiguous item moves are not bounded or do not distinguish critical from ordinary loot.'
}
Write-Output 'PASS ambiguous moves use two bounded sends; critical loot holds and ordinary loot settles'

if (-not $loot.Contains('UpdateRule(selected.Rule)') -or
    $loot.IndexOf('UpdateRule(selected.Rule)') -lt $loot.IndexOf('selected.Verified = true')) {
    throw 'Rule consumption occurs before verified acquisition.'
}
if (-not $loot.Contains('oneEachNames.Add(item.Name)') -or
    -not $loot.Contains('alreadyOwned + selectedForRule >= quantity')) {
    throw 'OneEach or Quantity source-snapshot semantics regressed.'
}
Write-Output 'PASS Quantity and OneEach are reserved per snapshot and consumed only after verification'

if (-not $loot.Contains('LootWindowDwellSeconds = 1.5') -or
    -not $loot.Contains('OpenedAt = Time.AONormalTime') -or
    -not $loot.Contains('Time.AONormalTime < _sourceAcquisition.OpenedAt + LootWindowDwellSeconds')) {
    throw 'The per-source 1.5 second ContainerOpened dwell regressed.'
}
if ($loot.Contains('selected.SentAt + LootWindowDwellSeconds')) {
    throw 'Visual dwell is incorrectly applied per item instead of per opened source.'
}
Write-Output 'PASS visual dwell starts at ContainerOpened, is per source, and does not gate move sends'

if (-not $loot.Contains('Inventory.NumFreeSlots <= 1') -or
    -not $loot.Contains('MissionSourceState.SkippedNoCapacity') -or
    -not $loot.Contains('MissionSourceState.CriticalBlocked')) {
    throw 'Low-space ordinary settlement or critical fail-closed behavior regressed.'
}
Write-Output 'PASS inventory pressure remains non-blocking for ordinary loot and fail-closed for critical loot'

if (-not $loot.Contains('DeferredApproachReady(x.Identity)') -or
    -not $loot.Contains('MaxOrdinaryApproachAttempts = 2') -or
    -not $loot.Contains('RetryAfterTraversal = _missionRoomTraversals + 1')) {
    throw 'Deferred/unreachable two-attempt semantics regressed.'
}
Write-Output 'PASS deferred unreachable-loot policy remains unchanged'

if (-not $dungeon.Contains('_loot.EndMissionRoom(restoreUserState: false)') -or
    -not $loot.Contains('if (restoreUserState) _enabledForMission = false')) {
    throw 'ManagerLoot is still restored/toggled during every room transition.'
}
Write-Output 'PASS ManagerLoot stays enabled across room transitions and restores user state at mission end'

if (-not $dungeon.Contains('ManagerLoot.ManagerLoot.UsableMissionItemIdentity(_objective.ReturnItem.UniqueIdentity)') -or
    -not $dungeon.Contains('_objective.ReturnItem.UniqueIdentity == item.UniqueIdentity)')) {
    throw 'A None/zero-like Return Item placeholder can again protect every ordinary source item with a None identity.'
}
if (-not $loot.Contains('identity != MissionIdentity.None') -or
    -not $loot.Contains('identity.Type != IdentityType.None') -or
    -not $loot.Contains('identity.Instance != 0')) {
    throw 'Return Item protection no longer uses the full usable-identity guard.'
}
Write-Output 'PASS None source identities reach ManagerLoot rules instead of matching an unusable Return Item placeholder'

if (-not $loot.Contains('ReferenceEquals(stored, item)') -or
    -not $loot.Contains('? stored.UniqueIdentity == item.UniqueIdentity') -or
    -not $loot.Contains(': ReferenceEquals(stored, item)')) {
    throw 'Protected-bag membership can again collide globally on an unusable item identity.'
}
if (-not $loot.Contains('RuleNameMatches(item.Name, item.Id, r.Name') -or
    -not $loot.Contains("ruleName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)") -or
    -not $loot.Contains(".All(word => itemName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)")) {
    throw 'Exact or partial-name ManagerLoot matching regressed for listed source items.'
}
Write-Output 'PASS None source items reach exact/partial rules while the observed protected-bag object remains protected'

$rememberStart = $loot.IndexOf('private void RememberSelectedIdentity(MissionIdentity identity)')
$rememberEnd = $loot.IndexOf('private bool SameSelectedItem(', $rememberStart)
$classifyStart = $loot.IndexOf('public ItemClassification Classify(Item item')
$classifyEnd = $loot.IndexOf('public string KeepBagName(', $classifyStart)
$resetStart = $loot.IndexOf('public void ResetMissionLootSkips()')
$resetEnd = $loot.IndexOf('private bool ReservedMissionLoot(', $resetStart)
if ($rememberStart -lt 0 -or $rememberEnd -lt 0 -or $classifyStart -lt 0 -or
    $classifyEnd -lt 0 -or $resetStart -lt 0 -or $resetEnd -lt 0) {
    throw 'Selected-item identity safety paths were not found.'
}
$remember = $loot.Substring($rememberStart, $rememberEnd - $rememberStart)
$classify = $loot.Substring($classifyStart, $classifyEnd - $classifyStart)
$reset = $loot.Substring($resetStart, $resetEnd - $resetStart)
if (-not $remember.Contains('if (HasUsableIdentity(identity))') -or
    -not $remember.Contains('_selectedItems.Add(identity)') -or
    ([regex]::Matches($loot, '_selectedItems\.Add\(').Count -ne 1)) {
    throw 'A selected-item insertion can bypass the shared usable-identity guard.'
}
if (-not $classify.Contains('HasUsableIdentity(item.UniqueIdentity) && _selectedItems.Contains(item.UniqueIdentity)') -or
    -not $classify.Contains('!HasUsableIdentity(item.UniqueIdentity)') -or
    -not $classify.Contains('_verifiedSelectedDetails.Any(selected => SameItemDetails(item, selected))')) {
    throw 'Classification no longer separates usable selected identities from exact-detail None identity retention.'
}
Write-Output 'PASS None identities cannot enter or globally match selected-item identity memory'

if (-not $loot.Contains('RememberSelectedIdentity(acquired.UniqueIdentity)') -or
    -not $loot.Contains('RememberSelectedIdentity(corpseItem.UniqueIdentity)') -or
    -not $loot.Contains('RememberSelectedIdentity(selected.UniqueIdentity)')) {
    throw 'Transactional or legacy selected-item memory bypasses the guarded helper.'
}
if (-not $reset.Contains('_selectedItems.RemoveWhere(identity => !HasUsableIdentity(identity))') -or
    $reset.Contains('_selectedItems.Clear()') -or $reset.Contains('_verifiedSelectedDetails.Clear()')) {
    throw 'Mission reset does not purge unusable sentinels while retaining post-mission selected ownership.'
}
Write-Output 'PASS reset purges contaminated sentinels without forgetting valid or exact-detail selected ownership'

if (-not $classify.Contains('bool listed = GetMatchingRule(item) != null') -or
    -not $classify.Contains('_settings["Reverse"].AsBool() ? !listed : listed')) {
    throw 'Reverse-off listed/unlisted classification semantics regressed.'
}
Write-Output 'PASS Reverse-off None items independently follow their own listed or unlisted rule result'
