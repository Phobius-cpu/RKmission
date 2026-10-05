param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root "RKmission/bin/$Configuration/net48/RKmission.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build RKmission/RKmission.csproj before running this check."
}

$assembly = [System.Reflection.Assembly]::LoadFrom($dll)
$policy = $assembly.GetType('RKmission.InventoryPolicy', $true)
$operationalName = $policy.GetMethod('IsBuiltInOperationalName',
    [System.Reflection.BindingFlags]'NonPublic,Static')
$bagFamily = $assembly.GetType('ManagerLoot.ManagedBagFamily', $true)
$protectedBag = $bagFamily.GetMethod('IsProtected',
    [System.Reflection.BindingFlags]'Public,Static')

function Assert-Equal($actual, $expected, $case) {
    if ($actual -ne $expected) { throw "$case`: expected $expected, got $actual" }
    Write-Output "PASS $case"
}

Assert-Equal ($operationalName.Invoke($null, @('Lock Pick'))) $true 'actual Lock Pick is operational'
Assert-Equal ($operationalName.Invoke($null, @('Eye Implant: Vehicle Air, Shiny'))) $false 'Vehicle Air implant is not operational by name'
Assert-Equal ($operationalName.Invoke($null, @('Nano Formula Recompiler'))) $false 'recompiler is not operational by name'
Assert-Equal ($operationalName.Invoke($null, @('Cluster Bullets'))) $false 'cluster bullets require a consumer registration'
Assert-Equal ($protectedBag.Invoke($null, @('RKM Keep 01'))) $true 'numbered Keep bag is protected'
Assert-Equal ($protectedBag.Invoke($null, @('RKM Mission'))) $true 'Mission bag is protected'
Assert-Equal ($protectedBag.Invoke($null, @('RKM Sell 01'))) $false 'Sell bag is not hard protected'

# AOSharp inventory and Item construction require a running AO client. Check
# the branch relationship in source so an unlisted Keep-bag item still wins
# over a removed or nonmatching ManagerLoot rule.
$source = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$reasonStart = $source.IndexOf('internal string ProtectionReason(Item item)')
$classifyStart = $source.IndexOf('public ItemClassification Classify(Item item', $reasonStart)
$classifyEnd = $source.IndexOf('public bool HasUnprocessedMissionLoot(', $classifyStart)
if ($reasonStart -lt 0 -or $classifyStart -lt 0 -or $classifyEnd -lt 0) {
    throw 'ManagerLoot protection/classification path was not found.'
}
$reason = $source.Substring($reasonStart, $classifyStart - $reasonStart)
$classify = $source.Substring($classifyStart, $classifyEnd - $classifyStart)
if (-not $reason.Contains('ManagedBagFamily.IsProtected(bag.Name)') -or
    -not $reason.Contains('stored.UniqueIdentity == item.UniqueIdentity') -or
    $reason.Contains('GetMatchingRule(') -or
    $classify.IndexOf('ProtectionReason(item)') -gt $classify.IndexOf('GetMatchingRule(item)')) {
    throw 'Keep-bag content protection no longer precedes ManagerLoot rules.'
}
Write-Output 'PASS RKM Keep content protection precedes missing ManagerLoot rule'
