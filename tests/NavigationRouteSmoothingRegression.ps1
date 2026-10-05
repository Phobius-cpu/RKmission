param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root "RKmission/bin/$Configuration/net48/RKmission.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build RKmission/RKmission.csproj before running this check."
}

$common = [System.Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $dll) 'AOSharp.Common.dll'))
$assembly = [System.Reflection.Assembly]::LoadFrom($dll)
$recorder = $assembly.GetType('RKmission.NavigationRouteRecorder', $true)
$simplify = $recorder.GetMethod('SimplifyRecordedPath',
    [System.Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $simplify) { throw 'Recorded navigation route simplifier was not found.' }
$vectorType = $common.GetType('AOSharp.Common.GameData.Vector3', $true)
$listType = [System.Collections.Generic.List``1].MakeGenericType($vectorType)

$data = Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/NavigationRoutes.json') -Raw |
    ConvertFrom-Json
$checked = 0
foreach ($route in $data.Routes) {
    if ($route.Name -notmatch '(?i)terminal.*(?:f?grid)') { continue }
    $points = [System.Activator]::CreateInstance($listType)
    foreach ($point in $route.Points) {
        $points.Add([System.Activator]::CreateInstance($vectorType,
            @([float]$point[0], [float]$point[1], [float]$point[2])))
    }
    $invokeArguments = [object[]]::new(1)
    $invokeArguments[0] = $points
    $smoothed = $simplify.Invoke($null, $invokeArguments)
    if ($smoothed.Count -lt 2) { throw "$($route.Name) lost an endpoint." }
    $distance = $vectorType.GetMethod('Distance', [System.Reflection.BindingFlags]'Public,Static')
    if ($distance.Invoke($null, @($points[0], $smoothed[0])) -gt 0.01 -or
        $distance.Invoke($null, @($points[$points.Count - 1], $smoothed[$smoothed.Count - 1])) -gt 0.01) {
        throw "$($route.Name) did not retain both recorded endpoints."
    }
    $legs = $smoothed.Count - 1
    if ($legs -gt 5 -or $legs -ge [Math]::Ceiling(($points.Count - 1) / 2.0)) {
        throw "$($route.Name) retained $legs legs for $($points.Count) samples."
    }
    $reverse = [System.Activator]::CreateInstance($listType)
    for ($i = $points.Count - 1; $i -ge 0; $i--) { $reverse.Add($points[$i]) }
    $invokeArguments[0] = $reverse
    $reverseSmoothed = $simplify.Invoke($null, $invokeArguments)
    if ($reverseSmoothed.Count - 1 -gt 5 -or
        $distance.Invoke($null, @($reverse[0], $reverseSmoothed[0])) -gt 0.01 -or
        $distance.Invoke($null, @($reverse[$reverse.Count - 1],
            $reverseSmoothed[$reverseSmoothed.Count - 1])) -gt 0.01) {
        throw "$($route.Name) reverse playback was not safely simplified."
    }
    Write-Output "PASS $($route.Name): $($points.Count) samples -> $legs forward legs, $($reverseSmoothed.Count - 1) reverse legs"
    $checked++
}
if ($checked -ne 3) { throw "Expected three bundled terminal-to-Grid routes, found $checked." }

$source = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
if (-not $source.Contains('candidate = SimplifyRecordedPath(candidate)') -or
    -not $source.Contains('corridor-verified leg(s)')) {
    throw 'Terminal-to-Grid playback does not use or report corridor simplification.'
}
Write-Output 'PASS user route overrides and bundled terminal-to-Grid routes share the playback simplifier'
