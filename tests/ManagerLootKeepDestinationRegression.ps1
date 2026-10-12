param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root "RKmission/bin/$Configuration/net48/RKmission.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build RKmission/RKmission.csproj before running this check."
}

$assembly = [System.Reflection.Assembly]::LoadFrom($dll)
$managerLoot = $assembly.GetType('ManagerLoot.ManagerLoot', $true)
$ruleType = $assembly.GetType('ManagerLoot.Rule', $true)
$resolver = $managerLoot.GetMethod('ResolveKeepDestination',
    [System.Reflection.BindingFlags]'NonPublic,Static')

function Resolve([string]$bagName) {
    $rule = [System.Activator]::CreateInstance($ruleType)
    $ruleType.GetField('BagName').SetValue($rule, $bagName)
    return $resolver.Invoke($null, @($rule))
}

function Assert-Equal($actual, $expected, $case) {
    if ($actual -ne $expected) { throw "$case`: expected '$expected', got '$actual'" }
    Write-Output "PASS $case"
}

Assert-Equal (Resolve '') 'RKM Keep' 'blank listed destination defaults to RKM Keep'
Assert-Equal (Resolve '   ') 'RKM Keep' 'legacy Inventory stored as blank defaults to RKM Keep'
Assert-Equal (Resolve 'RKM Keep') 'RKM Keep' 'explicit RKM Keep retains family semantics'
Assert-Equal (Resolve ' RKM Keep 02 ') 'RKM Keep 02' 'numbered Keep destination remains exact'
Assert-Equal (Resolve ' Custom Loot ') 'Custom Loot' 'custom destination remains explicit'

$source = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$recordStart = $source.IndexOf('private void RecordVerifiedTransfer(ListedItemTransfer selected)')
$recordEnd = $source.IndexOf('private bool ProcessMissionSourceAcquisition()', $recordStart)
if ($recordStart -lt 0 -or $recordEnd -lt 0) { throw 'Verified transfer path was not found.' }
$record = $source.Substring($recordStart, $recordEnd - $recordStart)
if (-not $source.Contains('return rule == null ? null : ResolveKeepDestination(rule);')) {
    throw 'KeepBagName no longer shares the verified-transfer destination policy.'
}
if (-not $record.Contains('if (!selected.Objective && selected.Rule != null)')) {
    throw 'Mission objective items can be forced into a Keep destination.'
}
if (-not $record.Contains('FirstOrDefault(candidate => candidate.Items.Count < 21)') -or
    -not $record.Contains("leaving '{selected.Name}' safely in main inventory as Keep")) {
    throw 'Missing/full destination does not safely retain the Keep item in main inventory.'
}
if ($record.Contains('Create') -or $record.Contains('new Container')) {
    throw 'Keep placement unexpectedly assumes automatic bag creation.'
}
if ($record.IndexOf('UpdateRule(selected.Rule)') -gt $record.IndexOf('selected.Verified = true') -and
    $record.IndexOf('ResolveKeepDestination(selected.Rule)') -gt $record.IndexOf('UpdateRule(selected.Rule)')) {
    Write-Output 'PASS destination movement follows verified acquisition and rule consumption'
} else {
    throw 'Destination resolution no longer follows verified acquisition and rule consumption.'
}
Write-Output 'PASS objectives stay in main and missing/full bags preserve Keep ownership without bag creation'
Write-Output 'PASS post-exit KeepBagName shares the blank-to-RKM-Keep destination policy'
