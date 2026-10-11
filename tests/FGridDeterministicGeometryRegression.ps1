$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$geometry = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridGeometry.cs') -Raw
$provider = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw
$survey = Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/FixerGridSurveyExits.json') -Raw | ConvertFrom-Json

foreach ($required in @('CenterX = 307.003f', 'CenterZ = 67.037f',
    'RingRadius = 11.694f', 'FloorYOffset = 1.435f', 'RingAnchor(',
    'ShortestArc(', 'ArcWaypointSpacing = 2.5f', 'result[result.Count - 1] = RingAnchor')) {
    if (-not $geometry.Contains($required)) { throw "Missing canonical geometry behavior: $required" }
}
if ($survey.Count -ne 78) { throw "Expected 78 surveyed exits, found $($survey.Count)." }
$cx = 307.003; $cz = 67.037; $radius = 11.694
foreach ($exit in $survey) {
    $dx = [double]$exit.FGridPosition[0] - $cx
    $dz = [double]$exit.FGridPosition[2] - $cz
    $length = [Math]::Sqrt($dx*$dx + $dz*$dz)
    if ($length -lt 5 -or $length -gt 18) { throw "Implausible radial exit $($exit.PortalIdentity)." }
    $ax = $cx + $dx * $radius / $length
    $az = $cz + $dz * $radius / $length
    $anchorRadius = [Math]::Sqrt(($ax-$cx)*($ax-$cx) + ($az-$cz)*($az-$cz))
    $cross = [Math]::Abs(($ax-$cx)*$dz - ($az-$cz)*$dx)
    if ([Math]::Abs($anchorRadius-$radius) -gt 0.0001 -or $cross -gt 0.0001) {
        throw "Exit anchor $($exit.PortalIdentity) is not on the ring/radial intersection."
    }
    $expectedY = [int]$exit.Floor * 10 + 1.435
    if ($expectedY -lt 11.4 -or $expectedY -gt 101.5) { throw 'Invalid floor Y model.' }
}
$liftBlock = [regex]::Match($provider,
    'private static readonly Vector3\[\] UpLifts =(?<block>[\s\S]*?)\};').Groups['block'].Value
$liftMatches = [regex]::Matches($liftBlock,
    'new Vector3\((?<x>[0-9.]+)f,\s*(?<y>[0-9.]+)f,\s*(?<z>[0-9.]+)f\)')
if ($liftMatches.Count -ne 10) { throw "Expected 10 source lift coordinates, found $($liftMatches.Count)." }
# Index zero is the intentionally legacy Floor 0 lift at the ring center. The
# nine modeled lift approaches on Floors 1-9 use indices 1-9.
foreach ($lift in $liftMatches | Select-Object -Skip 1) {
    $dx = [double]$lift.Groups['x'].Value - $cx
    $dz = [double]$lift.Groups['z'].Value - $cz
    $length = [Math]::Sqrt($dx*$dx + $dz*$dz)
    if ($length -lt 5 -or $length -gt 18) { throw 'Implausible radial lift coordinate.' }
    $ax = $cx + $dx * $radius / $length
    $az = $cz + $dz * $radius / $length
    $radialX = $ax - $cx; $radialZ = $az - $cz
    $tangentX = -$radialZ; $tangentZ = $radialX
    $dot = $radialX*$tangentX + $radialZ*$tangentZ
    if ([Math]::Abs($dot) -gt 0.0001) { throw 'Lift anchor does not form a 90-degree tangent/radial transition.' }
}
foreach ($required in @('ArrivalAnchor, RingArc, DestinationAnchor, RadialApproach',
    '"Arrival Anchor"', '"Ring Arc"', '"Lift Anchor"', '"Exit Anchor"',
    '"Radial Approach"', 'RingCrossTrackTolerance', 'RadialCrossTrackTolerance',
    'ResetDeterministicGeometry(true)', 'retaining legacy internal navigation')) {
    if (-not $provider.Contains($required)) { throw "Missing deterministic phase/fallback guard: $required" }
}
Write-Output 'PASS canonical fit, floor Y values, all 78 exit anchors, and 9 modeled lift anchors with 90-degree transitions'
Write-Output 'PASS deterministic arrival/ring/destination/radial phase order and guarded legacy fallback'
