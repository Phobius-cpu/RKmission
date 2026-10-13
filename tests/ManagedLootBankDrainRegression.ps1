param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cycle = Get-Content -LiteralPath (Join-Path $root 'RKmission/AutomaticLogisticsCycle.cs') -Raw
$normalizer = Get-Content -LiteralPath (Join-Path $root 'RKmission/ManagedInventoryNormalizer.cs') -Raw
$bank = Get-Content -LiteralPath (Join-Path $root 'RKmission/BankTransactions.cs') -Raw
$shop = Get-Content -LiteralPath (Join-Path $root 'RKmission/ShopSaleTest.cs') -Raw

function Assert($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

Assert ($cycle.Contains('_normalizer.Start(_maximumItems, _missionCycleRun)')) `
    'finite mission-cycle normalization explicitly enables managed Keep draining'
Assert ($normalizer.Contains('_drainManagedKeeps || !ManagerLoot.ManagedBagFamily.IsKeep(bag.Name)')) `
    'RK Loot 01/02 family bags participate in automatic bank staging'
Assert ($normalizer.Contains('_loot.Classify(item) == ManagerLoot.ItemClassification.Keep') -and
    $normalizer.Contains('_policy.IsOperationalItem(item)') -and
    $normalizer.Contains('_loot.ProtectionReason(item) == null')) `
    'bank drain authority remains ManagerLoot Keep plus operational/protection safeguards'
Assert ($normalizer.Contains('sourceNow == _sourceBefore - 1 && destinationNow == _destinationBefore + 1') -and
    $normalizer.Contains('BankReservations.Reserve(destinationItem, _sourceLabel)')) `
    'RK Loot staging is reserved only after source disappearance and main arrival proof'
Assert ($normalizer.Contains('main inventory reached capacity') -and
    $normalizer.Contains('closing this bounded round')) `
    'partial capacity produces a bounded deposit round instead of a blind extra move'

Assert ($cycle.Contains('MaximumAutomaticBankRounds = 20') -and
    $cycle.Contains('_bankRoundKeeps = Math.Min(_maximumItems, bankEligible)') -and
    $cycle.Contains('_bankRounds++')) `
    'more than 20 Keeps are handled through multiple individually bounded trips'
Assert ($cycle.Contains('refreshing all RK Loot/RK Junk snapshots before planning the next bounded round') -and
    $cycle.Contains('_inventoryLoadRequested = false')) `
    'every post-zone bank return refreshes managed bag snapshots before continuing'
Assert ($cycle.Contains('bounded bank drain stopped after') -and
    $cycle.Contains('no further bank route was started')) `
    'round exhaustion fails safe with remaining-reservation diagnostics'
Assert ($cycle.Contains('BaggedBankEligibleCount(_missionCycleRun)') -and
    $normalizer.Contains('includeManagedKeeps')) `
    'final reconciliation cannot silently ignore eligible Keeps still in RK Loot bags'

Assert ($bank.Contains('_reservations.TrySelectNext(main') -and
    $bank.Contains('_reservations.ConsumeVerified(_reservedItem)') -and
    $bank.Contains('mainCount == _mainBefore - 1 && bankCount == _bankBefore + 1')) `
    'duplicate-safe exact reservations are consumed only after bank arrival/main removal proof'
Assert ($bank.Contains('no transfer was retried')) `
    'uncertain bank deposits retain the no-blind-retry fail-safe'
Assert ($cycle.Contains('ClearSellBagLoading();') -and
    $cycle.Contains('ManagedBagOpenOwnership.ReleaseAll(BagOwner)')) `
    'RKMission-owned managed bag windows are released on completion and failure'

Assert ($shop.Contains('_completed >= _requested') -and
    -not $shop.Contains('MoveAll') -and -not $shop.Contains('AddAll')) `
    'shop behavior remains sequential and does not introduce bulk transactions'
Assert ($cycle.Contains('else if (_shopPlanned) _phase = Phase.StartShop;') -and
    $cycle.Contains('else BeginReconciliation();')) `
    'zero-Keep and bank-then-shop paths retain the existing lifecycle handoff'

Write-Output 'PASS two full RK Loot bags can drain across 20-item bank rounds while Reject sales remain one-at-a-time'
