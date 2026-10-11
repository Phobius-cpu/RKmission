$ErrorActionPreference = 'Stop'
$fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/SideRampsTopology.json') -Raw | ConvertFrom-Json

function Distance($a, $b) {
    [Math]::Sqrt([Math]::Pow($b[0] - $a[0], 2) +
        [Math]::Pow($b[1] - $a[1], 2) + [Math]::Pow($b[2] - $a[2], 2))
}
function Length($points) {
    $total = 0.0
    for ($i = 1; $i -lt $points.Count; $i++) { $total += Distance $points[$i - 1] $points[$i] }
    $total
}

$ascend = @($fixture.Ascend)
$descend = @($fixture.Descend)
if ($ascend.Count -lt 8 -or $descend.Count -lt 8) { throw 'Side-ramp evidence lost ordered switchback anchors.' }
if ([Math]::Abs($ascend[0][1] - 5.01) -gt 0.1 -or
    [Math]::Abs($ascend[-1][1] - 13.005) -gt 0.1) { throw 'Ascent no longer spans lower and upper galleries.' }
if ((Distance $ascend[0] $ascend[-1]) -gt 12 -or (Length $ascend) -lt 60) {
    throw 'Fixture no longer proves a long switchback between spatially close endpoints.'
}
if ($ascend[2][2] -lt 120 -or $ascend[4][2] -gt 106 -or $ascend[6][2] -lt 120) {
    throw 'Fixture lost the two ramp reversals and middle-gallery return.'
}
if ((Distance $ascend[0] $descend[-1]) -gt 1 -or
    (Distance $ascend[-1] $descend[0]) -gt 1) {
    throw 'Ascent and descent recordings no longer agree at both endpoints.'
}
Write-Output 'PASS side-ramp fixture proves an ordered two-stage switchback with matching bidirectional endpoints'
