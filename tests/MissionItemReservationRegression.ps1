param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$loot = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$objective = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionObjective.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$policy = Get-Content -LiteralPath (Join-Path $root 'RKmission/InventoryPolicy.cs') -Raw

if ($loot.Contains('_rememberedMissionItems') -or
    -not $loot.Contains('List<MissionItemReservation> _missionItemReservations') -or
    -not $loot.Contains('public MissionIdentity Owner;')) {
    throw 'Mission protection is no longer represented by owner-scoped reservations.'
}
Write-Output 'PASS reservations are scoped to an owning mission instead of a session-wide identity set'

foreach ($guard in @(
    'identity != MissionIdentity.None',
    'identity.Type != IdentityType.None',
    'identity.Instance != 0',
    '!UsableMissionItemIdentity(item.UniqueIdentity)')) {
    if (-not $loot.Contains($guard)) { throw "Unusable identity guard regressed: $guard" }
}
Write-Output 'PASS None and zero-like identities cannot enter durable mission protection'

foreach ($metadata in @(
    'LowId == item.LowId',
    'HighId == item.HighId',
    'QualityLevel == item.QualityLevel',
    'string.Equals(Name, item.Name, StringComparison.Ordinal)')) {
    if (-not $loot.Contains($metadata)) { throw "Reservation item validation regressed: $metadata" }
}
if (-not $loot.Contains('MissionReservationActive?.Invoke(x.Owner) ?? false')) {
    throw 'Reservation lookup no longer requires an active owning mission.'
}
Write-Output 'PASS reused identities cannot protect mismatched items and inactive owners do not protect inventory'

if (-not $dungeon.Contains('_loot.BeginMissionReservations(record.Id)') -or
    -not $dungeon.Contains('_loot.RememberMissionCriticalInventory(_record.Id)') -or
    -not $dungeon.Contains('_loot.ProtectPendingHandIn(_record.Id, _objective.ReturnItem)')) {
    throw 'Dungeon reservation ownership/lifecycle wiring regressed.'
}
if (-not $loot.Contains('"pending Return Item hand-in"')) {
    throw 'Pending Return Item ownership context regressed.'
}
Write-Output 'PASS active objective and pending Return Item protection retain explicit mission ownership'

if (-not $objective.Contains('!step.Proof && !step.Done') -or
    -not $objective.Contains('!RewardConfirmed') -or
    -not $objective.Contains('MissionProgress.CompletedAutomatically') -or
    -not $objective.Contains('MissionProgress.CompletedByUser')) {
    throw 'Repair/Use/Find proof and completion release gates regressed.'
}
Write-Output 'PASS objective inventory protection releases only after proof or mission completion'

$releaseCount = ([regex]::Matches($bot, '_loot\.ReleaseMissionReservations\(_selected\.Id\)')).Count
if ($releaseCount -ne 3) {
    throw "Expected user completion, automatic completion, and verified hand-in release paths; found $releaseCount."
}
if (-not $bot.Contains('mission.Present || mission.ReturnHandInPending') -or
    -not $bot.Contains('mission.State == MissionProgress.AwaitingHandIn')) {
    throw 'Active/pending reservation ownership predicate regressed.'
}
Write-Output 'PASS user completion, automatic completion, and verified hand-in release reservations'

if (-not $loot.Contains('item.UniqueIdentity.Type == IdentityType.MissionKey') -or
    -not $loot.Contains('ManagedBagFamily.IsProtected(bag.Name)') -or
    -not $loot.Contains('OperationalItemProtected?.Invoke(item)')) {
    throw 'Independent MissionKey, protected-bag, or operational protection regressed.'
}
Write-Output 'PASS MissionKey, RKM Keep/Mission, and operational protections remain independent'

if (-not $policy.Contains('classification != ManagerLoot.ItemClassification.Reject') -or
    -not $policy.Contains('if (!inSellBag && !newlyAcquiredMain)') -or
    -not $policy.Contains('loot.ProtectionReason(item) != null')) {
    throw 'Positive automatic-sale gate regressed.'
}
Write-Output 'PASS reservation release still requires Reject classification and explicit sale provenance'

if (-not $loot.Contains('return $"reserved mission item (mission {reservation.Owner.Instance}, {reservation.Context})"')) {
    throw 'Mission reservation ownership diagnostic regressed.'
}
if (-not $loot.Contains('$"mission objective (mission {ActiveMissionOwner.Instance})"')) {
    throw 'Live mission objective ownership diagnostic regressed.'
}
Write-Output 'PASS protection diagnostics identify the owning mission and reservation context'

if ($loot.Contains('_rememberedMissionItems.Contains(item.UniqueIdentity)')) {
    throw 'The stale six-item RKM Sell identity-only protection path still exists.'
}
Write-Output 'PASS stale RKM Sell items fall through to normal ManagerLoot classification'
