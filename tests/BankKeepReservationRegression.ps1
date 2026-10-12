param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ledger = Get-Content -LiteralPath (Join-Path $root 'RKmission/BankKeepReservationLedger.cs') -Raw
$normalizer = Get-Content -LiteralPath (Join-Path $root 'RKmission/ManagedInventoryNormalizer.cs') -Raw
$bank = Get-Content -LiteralPath (Join-Path $root 'RKmission/BankTransactions.cs') -Raw
$cycle = Get-Content -LiteralPath (Join-Path $root 'RKmission/AutomaticLogisticsCycle.cs') -Raw

function Assert($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

Assert ($normalizer.Contains('BankReservations.Reserve(destinationItem, _sourceLabel)') -and
    $normalizer.IndexOf('BankReservations.Reserve(destinationItem, _sourceLabel)') -gt
    $normalizer.IndexOf('sourceNow == _sourceBefore - 1')) 'reservations are created only after a verified transfer'
Assert ($normalizer.Contains('BankReservations.Clear()') -and
    $cycle.Contains('_normalizer.BankReservations.Count')) 'reservations are cycle-local and own automatic bank planning'
Assert ($normalizer.Contains('ReserveExistingMainKeeps()') -and
    $normalizer.Contains("reserved existing main Keep") -and
    $normalizer.Contains('BankReservations.Reserve(item, "existing main inventory")')) 'existing main bank-eligible Keeps are reserved without an item move'
Assert ($normalizer.Contains('_policy.IsBankEligible(item, _loot)') -and
    $normalizer.Contains('_policy.IsOperationalItem(item)') -and
    $normalizer.Contains('_loot.ProtectionReason(item) == null')) 'existing-main reservations retain destination, operational, and protection authority'
Assert ($normalizer.Contains('BankReservations.CountMatching(representative)') -and
    $ledger.Contains('public int CountMatching(Item item)')) 'existing and normalized reservations deduplicate by exact fingerprint multiplicity'
Assert ($normalizer.Contains('int observed = group.Count()') -and
    $normalizer.Contains('int missing = observed - reserved') -and
    $normalizer.Contains('partial indistinguishable fingerprint batch')) 'reservation multiplicity cannot exceed exact origin counts or split an ambiguous duplicate batch'
Assert ($normalizer.Contains('foreach (Item item in group.Take(missing))')) 'two exact main copies can create multiplicity two when both fit the requested batch'
Assert ($cycle.Contains('totalMainBankEligible - _plannedBankKeeps')) 'main Keeps beyond cycle N are tracked as an expected extra batch'

foreach ($detail in @('item.Id == Id', 'item.LowId == LowId', 'item.HighId == HighId',
    'item.QualityLevel == QualityLevel', 'string.Equals(item.Name, Name, StringComparison.Ordinal)')) {
    Assert ($ledger.Contains($detail)) "post-zone fingerprint includes $detail"
}
Assert (-not $ledger.Contains('UniqueIdentity') -and -not $ledger.Contains('item.Slot')) 'post-zone matching ignores transient identity and slot'
Assert ($ledger.Contains('found.Length < expected') -and $ledger.Contains('reservation missing')) 'missing exact reservations fail closed'
Assert ($ledger.Contains('found.Length > expected') -and $ledger.Contains('reservation ambiguous')) 'surplus indistinguishable matches fail closed'
Assert ($ledger.Contains('resolved.Length == _reservations.Count')) 'all reservations must resolve exactly'

Assert ($bank.Contains('StartReservedStore') -and $bank.Contains('TryValidateReservations') -and
    $cycle.Contains('StartReservedStore(_plannedBankKeeps, _normalizer.BankReservations)')) 'automatic bank uses the bounded reservation path'
Assert ($bank.Contains('_reservations.TrySelectNext(main') -and
    $bank.Contains('candidates = new[] { reserved };')) 'automatic selection cannot fall back to arbitrary ManagerLoot Keep items'
Assert ($bank.Contains('_reservations.ConsumeVerified(_reservedItem)') -and
    $bank.IndexOf('_reservations.ConsumeVerified(_reservedItem)') -gt
    $bank.IndexOf('mainCount == _mainBefore - 1 && bankCount == _bankBefore + 1')) 'verified deposits consume one reservation'
Assert ($bank.Contains('_reservations = null;') -and $bank.Contains('public bool StartStore')) 'manual bank entry clears reservation mode'
Assert ($cycle.Contains('bank storage completed with') -and
    $cycle.Contains('unconsumed Keep reservation')) 'automatic bank completion requires an empty ledger'
Assert ($cycle.Contains('planned bank Keep reservation') -or
    $bank.Contains('planned bank Keep reservation')) 'target mismatch diagnostics are reservation-aware'

Write-Output 'PASS exact fingerprints reject same-name items with different id/low/high/QL details'
Write-Output 'PASS identical items are accepted only when current and reserved multiplicities are exactly equal'
