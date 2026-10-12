param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$normalizer = Get-Content -LiteralPath (Join-Path $root 'RKmission/ManagedInventoryNormalizer.cs') -Raw

function Assert($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

$restore = $normalizer.IndexOf('if (!RestoreDeferredMainKeeps()) return;')
$place = $normalizer.IndexOf('if (!PlaceExistingMainKeep()) return;')
$reserve = $normalizer.IndexOf('if (!ReserveExistingMainKeeps()) return;')
Assert ($restore -ge 0 -and $restore -lt $place -and $place -lt $reserve) 'deferred restore, existing-main placement, and bank fallback retain strict precedence'

$methodStart = $normalizer.IndexOf('private bool PlaceExistingMainKeep()')
$methodEnd = $normalizer.IndexOf('private bool ReserveExistingMainKeeps()', $methodStart)
Assert ($methodStart -ge 0 -and $methodEnd -gt $methodStart) 'existing-main Keep placement phase exists'
$method = $normalizer.Substring($methodStart, $methodEnd - $methodStart)

Assert ($method.Contains('_loot.Classify(item) == ManagerLoot.ItemClassification.Keep')) 'only current ManagerLoot Keeps enter existing-main placement'
Assert ($method.Contains('!_policy.IsOperationalItem(item)') -and
    $method.Contains('_loot.ProtectionReason(item) == null')) 'operational and protected main items are excluded'
Assert ($method.Contains('string requested = _loot.KeepBagName(item)')) 'placement uses the current requested Keep destination'
Assert ($method.Contains('ManagerLoot.ManagedBagFamily.Matches(bag.Name, requested)') -and
    $method.Contains('bag.Items.Count < 21')) 'placement requires a matching destination with capacity'
Assert ($method.Contains('ManagerLoot.ManagedBagFamily.Order(bag.Name, requested)') -and
    $method.Contains('ThenBy(bag => bag.Name, StringComparer.OrdinalIgnoreCase)')) 'canonical-before-legacy and deterministic destination ordering are retained'
Assert ($method.Contains('bool preserveOwnership = !ManagerLoot.ManagedBagFamily.IsKeep(destination.Name)') -and
    $method.Contains('Prepare(item, Identity.None, destination.Identity, false, preserveOwnership, destination.Name)') -and
    $method.Contains('item.MoveToContainer(destination)')) 'main-to-bag placement uses verified transfer and preserves ownership only for non-protected custom destinations'
Assert ($method.Contains("moving existing main Keep '{item.Name}' to {destination.Name}")) 'placement emits the AO retest marker with the exact destination bag'

Assert ($normalizer.Contains('sourceNow == _sourceBefore - 1 && destinationNow == _destinationBefore + 1')) 'verification requires exact source and destination count deltas'
foreach ($detail in @('item.Id == _itemId', 'item.LowId == _itemLowId', 'item.HighId == _itemHighId',
    'item.QualityLevel == _itemQl', 'string.Equals(item.Name, _itemName, StringComparison.Ordinal)')) {
    Assert ($normalizer.Contains($detail)) "verified transfer fingerprint includes $detail"
}
Assert ($normalizer.Contains('Item destinationItem = DestinationItems().FirstOrDefault') -and
    $normalizer.Contains('_destinationSlotsBefore.Contains(item.Slot)')) 'destination arrival requires exact new-slot evidence'
Assert ($normalizer.Contains('_source == Identity.None ? MainItems()')) 'Identity.None safely identifies main as the source for main-to-bag verification'
Assert ($normalizer.Contains('if (DateTime.UtcNow - _sent > TimeSpan.FromSeconds(8))') -and
    -not $method.Contains('MoveToContainer(destination);`r`n                item.MoveToContainer(destination)')) 'uncertain transfers fail closed without retry'

Assert ($method.Contains('if (destination == null) continue;') -and
    $normalizer.Contains('_policy.IsBankEligible(item, _loot)')) 'missing or full destinations fall through to existing bank eligibility and reservation'
Assert ($normalizer.Contains('BankReservations.CountMatching(representative)')) 'bank reservations remain exact-fingerprint deduplicated'
Assert ($normalizer.Contains('current RK Loot destination takes precedence over bank fallback')) 'deferred Keep destination restoration remains active'
Assert ($normalizer.Contains('Keep remains in RK Junk') -and
    $normalizer.Contains('shop execution is blocked')) 'RK Junk shop blocker remains fail-closed'

Write-Output 'PASS default RK Loot, canonical/legacy family, numbered exact, and custom destinations share ManagedBagFamily matching semantics'
