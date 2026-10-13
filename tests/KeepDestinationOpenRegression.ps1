param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$closeStart = $source.IndexOf('case ProcessState.Close_Corpse:')
$closeEnd = $source.IndexOf('#endregion', $closeStart)
$close = $source.Substring($closeStart, $closeEnd - $closeStart)

Assert ($source.Contains('Move_To_KeepBag') -and
    $close.Contains('CurrentProcess = placeKeeps ? ProcessState.Move_To_KeepBag : ProcessState.Open_Corpse')) `
    'Keep placement is not deferred until after source settlement and closure.'
Write-Output 'PASS source settles and closes before destination-bag placement begins'

Assert ($source.Contains("Opening Keep destination '{selected.KeepBagName}' before placement") -and
    $source.Contains('_keepPlacementBagOpened = true') -and
    $source.Contains('MaxManagedBagLoadAttempts')) `
    'Destination placement does not require bounded ContainerOpened evidence.'
Write-Output 'PASS unopened or empty destination bags receive bounded positive open evidence'

$placementStart = $source.IndexOf('private bool ProcessKeepPlacement(ListedItemTransfer selected)')
$placementEnd = $source.IndexOf('private void RecordVerifiedTransfer', $placementStart)
$placement = $source.Substring($placementStart, $placementEnd - $placementStart)
Assert ($placement.IndexOf('_keepPlacementBagOpened') -lt $placement.IndexOf('acquired.MoveToContainer(bag)') -and
    $placement.Contains('MatchingBagCount(bag, selected) > selected.KeepBagCountBefore') -and
    $placement.Contains('bool leftMain = acquired == null')) `
    'A destination move can occur before bag-open evidence or bypass exact transfer verification.'
Write-Output 'PASS placement attempts start only after bag open and still require exact departure/arrival evidence'

Assert ($source.Contains('CurrentProcess == ProcessState.Move_To_KeepBag') -and
    $source.Contains("item remains safely owned in main inventory")) `
    'Deferred placement can release room ownership or lose safe main-inventory fallback.'
Write-Output 'PASS room processing ownership and safe fallback cover delayed destination placement'
