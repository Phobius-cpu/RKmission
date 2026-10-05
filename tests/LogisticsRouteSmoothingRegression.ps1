param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root "RKmission/bin/$Configuration/net48/RKmission.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build RKmission/RKmission.csproj before running this check."
}

$assembly = [System.Reflection.Assembly]::LoadFrom($dll)
$stageType = $assembly.GetType('RKmission.LogisticsRouteCatalog+Stage', $true)
$navigator = $assembly.GetType('RKmission.LogisticsRouteNavigator', $true)
$safe = $navigator.GetMethod('RecordedShortcutSafe',
    [System.Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $safe) { throw 'Recorded shortcut validator was not found.' }
$reverses = $navigator.GetMethod('ReversesNearDoorway',
    [System.Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $reverses) { throw 'Doorway direction validator was not found.' }

$data = Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/LogisticsRoutes.json') -Raw |
    ConvertFrom-Json
$dense = 0
foreach ($route in $data.Routes) {
    foreach ($source in $route.Stages) {
        if ($source.Points.Count -lt 2) { continue }
        $stage = [System.Activator]::CreateInstance($stageType, $true)
        $points = [System.Collections.Generic.List[float[]]]::new()
        foreach ($point in $source.Points) {
            $points.Add([float[]]@($point[0], $point[1], $point[2]))
        }
        $stageType.GetProperty('Points').SetValue($stage, $points)
        if ($route.Site -eq 'icc' -and $route.Purpose -eq 'bank' -and
            $source.Name -eq 'ToEntry' -and $reverses.Invoke($null, @($stage))) {
            throw 'ICC bank ToEntry still reverses at its recorded doorway.'
        }
        $at = 0
        $legs = 0
        while ($at -lt $points.Count - 1) {
            $next = $at + 1
            for ($candidate = $points.Count - 1; $candidate -gt $at + 1; $candidate--) {
                if ($safe.Invoke($null, @($stage, $at, $candidate))) {
                    $next = $candidate
                    break
                }
            }
            if ($next -le $at) { throw "$($route.Site)/$($route.Purpose) $($source.Name) did not advance." }
            $at = $next
            $legs++
        }
        if ($at -ne $points.Count - 1) {
            throw "$($route.Site)/$($route.Purpose) $($source.Name) did not retain its endpoint."
        }
        if ($points.Count -ge 20) {
            $dense++
            if ($legs -gt 10 -or $legs -ge [Math]::Ceiling($points.Count / 2.0)) {
                throw "$($route.Site)/$($route.Purpose) $($source.Name) retained $legs legs for $($points.Count) samples."
            }
        }
        Write-Output "PASS $($route.Site)/$($route.Purpose) $($source.Name): $($points.Count) samples -> $legs geometric legs"
    }
}
if ($dense -lt 8) { throw "Expected at least eight dense logistics stages, found $dense." }

$sourceCode = Get-Content -LiteralPath (Join-Path $root 'RKmission/LogisticsRouteNavigator.cs') -Raw
if (-not $sourceCode.Contains('!SMovementController.IsNavigating()') -or
    $sourceCode.Contains('SMovementController.IsNavigating() ?')) {
    throw 'Active logistics movement may be periodically resubmitting its destination.'
}
Write-Output 'PASS active logistics movement is not periodically resubmitted'
if (-not $sourceCode.Contains('BankArrivalTolerance = 1.25f') -or
    -not $sourceCode.Contains('OriginArrivalTolerance = 2f') -or
    -not $sourceCode.Contains('_route.Purpose == "bank" ? BankArrivalTolerance : ShopArrivalTolerance')) {
    throw 'Logistics endpoint approach tolerances are missing or no longer purpose-aware.'
}
Write-Output 'PASS bank and mission-terminal endpoint approaches use tightened tolerances'

$cycle = $assembly.GetType('RKmission.AutomaticLogisticsCycle', $true)
$result = $assembly.GetType('RKmission.VerifiedOperationResult', $true)
if ($null -eq $cycle.GetProperty('Result') -or $cycle.GetProperty('Result').PropertyType -ne $result) {
    throw 'Automatic logistics cycle does not expose its verified result.'
}
foreach ($typeName in @('LogisticsRouteNavigator', 'BankTransactions', 'ShopSaleTest')) {
    $type = $assembly.GetType("RKmission.$typeName", $true)
    if ($null -eq $type.GetProperty('Result') -or $type.GetProperty('Result').PropertyType -ne $result) {
        throw "$typeName does not expose a machine-readable verified result."
    }
}
Write-Output 'PASS automatic logistics phases expose machine-readable verified outcomes'
