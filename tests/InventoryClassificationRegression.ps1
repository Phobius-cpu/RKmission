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
$managerLoot = $assembly.GetType('ManagerLoot.ManagerLoot', $true)
$sameProtectedBagItem = $managerLoot.GetMethod('SameProtectedBagItem',
    [System.Reflection.BindingFlags]'NonPublic,Static')
$ruleNameMatches = $managerLoot.GetMethod('RuleNameMatches',
    [System.Reflection.BindingFlags]'NonPublic,Static')
$coreAssembly = [System.Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $dll) 'AOSharp.Core.dll'))
$commonAssembly = [System.Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $dll) 'AOSharp.Common.dll'))
$itemType = $coreAssembly.GetType('AOSharp.Core.Inventory.Item', $true)
$identityType = $commonAssembly.GetType('AOSharp.Common.GameData.Identity', $true)
$identityKind = $commonAssembly.GetType('AOSharp.Common.GameData.IdentityType', $true)
$policyInstance = [System.Activator]::CreateInstance($policy, $true)

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
Assert-Equal ($policy.GetProperty('AutomaticSellTriggerItems').GetValue($policyInstance)) 15 'automatic Sell pressure defaults to 15 Rejects'
Assert-Equal ($policy.GetProperty('AutomaticLogisticsItemLimit').GetValue($policyInstance)) 20 'automatic logistics defaults to 20 items per destination'

$none = [System.Activator]::CreateInstance($identityType, @([System.Enum]::Parse($identityKind, 'None'), 0))
$slotA = [System.Activator]::CreateInstance($identityType, @([System.Enum]::Parse($identityKind, 'Inventory'), 64))
$slotB = [System.Activator]::CreateInstance($identityType, @([System.Enum]::Parse($identityKind, 'Inventory'), 65))
$usable = [System.Activator]::CreateInstance($identityType, @([System.Enum]::Parse($identityKind, 'WeaponInstance'), 12345))
function New-TestItem($identity, $slot) {
    $item = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($itemType)
    $itemType.GetField('UniqueIdentity').SetValue($item, $identity)
    $itemType.GetField('Slot').SetValue($item, $slot)
    return $item
}
$usableOtherSlot = New-TestItem $usable $slotB
$usableSource = New-TestItem $usable $slotA
$noneStored = New-TestItem $none $slotA
$noneSource = New-TestItem $none $slotB
Assert-Equal ($sameProtectedBagItem.Invoke($null, @($usableSource, $usableOtherSlot))) $true 'usable identity proves protected-bag membership across snapshots'
Assert-Equal ($sameProtectedBagItem.Invoke($null, @($noneSource, $noneStored))) $false 'unrelated None identity does not inherit protected-bag membership'
Assert-Equal ($sameProtectedBagItem.Invoke($null, @($noneStored, $noneStored))) $true 'actual protected-bag snapshot item remains protected with None identity'
Assert-Equal ($ruleNameMatches.Invoke($null, @('Pharma Tech Tutoring Device', 55682, 'Tutoring Device', $false))) $true 'partial Tutoring Device rule matches live item'
Assert-Equal ($ruleNameMatches.Invoke($null, @('Burst of Speed Stim', 85368, 'Burst of Speed Stim', $true))) $true 'Burst of Speed Stim exact rule matches live item'
Assert-Equal ($ruleNameMatches.Invoke($null, @('Bow Construction Kit', 137213, 'Tutoring Device', $false))) $false 'unlisted live item does not match unrelated rule'

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
if (-not $source.Contains('ManagedBagFamily.IsProtected(bag.Name)') -or
    -not $source.Contains('HasUsableIdentity(item.UniqueIdentity)') -or
    -not $source.Contains('ReferenceEquals(stored, item)') -or
    -not $reason.Contains('ProtectedBagContains(item)') -or
    $reason.Contains('GetMatchingRule(') -or
    $classify.IndexOf('ProtectionReason(item)') -gt $classify.IndexOf('GetMatchingRule(item)')) {
    throw 'Keep-bag content protection no longer precedes ManagerLoot rules.'
}
Write-Output 'PASS RKM Keep content protection precedes missing ManagerLoot rule'
