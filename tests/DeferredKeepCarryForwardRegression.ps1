param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ledger = Get-Content -LiteralPath (Join-Path $root 'RKmission/DeferredKeepLedger.cs') -Raw
$normalizer = Get-Content -LiteralPath (Join-Path $root 'RKmission/ManagedInventoryNormalizer.cs') -Raw
$cycle = Get-Content -LiteralPath (Join-Path $root 'RKmission/AutomaticLogisticsCycle.cs') -Raw

function Assert($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

foreach ($detail in @('item.Id == Id', 'item.LowId == LowId', 'item.HighId == HighId',
    'item.QualityLevel == QualityLevel', 'string.Equals(item.Name, Name, StringComparison.Ordinal)')) {
    Assert ($ledger.Contains($detail)) "deferred fingerprint includes $detail"
}
Assert (-not $ledger.Contains('UniqueIdentity') -and -not $ledger.Contains('item.Slot')) 'deferred matching ignores transient identity and slot'
Assert ($ledger.Contains('ExpectedCount = group.Count()')) 'deferred ledger records exact multiplicity'
Assert ($cycle.Contains('RetainDeferredMainKeeps()')) 'successful reconciliation records current extra main Keeps'
Assert ($cycle.Contains('deferred to next logistics cycle')) 'reconciliation reports explicit next-cycle retention'
Assert ($normalizer.Contains('RestoreDeferredMainKeeps()') -and
    $normalizer.IndexOf('RestoreDeferredMainKeeps()') -lt $normalizer.IndexOf('ReserveExistingMainKeeps()')) 'deferred Keeps seed reservations before current ManagerLoot Keeps'
Assert ($normalizer.Contains('matches.Length == 0') -and $normalizer.Contains('expired deferred Keep')) 'zero exact matches expire safely'
Assert ($normalizer.Contains('matches.Length != entry.ExpectedCount') -and $normalizer.Contains('deferred Keep ambiguity')) 'surplus or missing multiplicity refuses adoption'
Assert ($normalizer.Contains('entry.ExpectedCount > remaining') -and $normalizer.Contains('will not split')) 'exact duplicate batches are never partially restored'
Assert ($normalizer.Contains('BankReservations.Reserve(item, "deferred Keep carry-forward")')) 'exact deferred matches seed cycle-local bank reservations'
Assert ($normalizer.Contains('BankReservations.CountMatching(representative)')) 'current ManagerLoot recognition deduplicates restored reservations'
Assert ($normalizer.Contains('_policy.IsOperationalItem(item)') -and $normalizer.Contains('_loot.ProtectionReason(item)')) 'operational and protected items expire instead of banking'
Assert ($normalizer.Contains('current RK Loot destination takes precedence over bank fallback')) 'newly available Keep destination overrides deferred bank fallback'
Assert ($cycle.Contains('ExpireDepositedDeferredKeeps()')) 'verified bank completion consumes absent deferred matches'
Assert ($cycle.Contains('_deferredKeeps.Clear()')) 'plugin teardown clears session-only carry-forward'
Assert ($cycle.Contains('bank Keep total planned={_plannedBankKeeps}')) 'restored reservation contributes to the next cycle bank plan'

Write-Output 'PASS expected multiplicity two restores only when exactly two current items match'
Write-Output 'PASS deferred authority is session-local and never broadens generic Keep classification'
