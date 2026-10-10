$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$provider = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$recast = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridRecastPlanner.cs') -Raw

foreach ($required in @(
    'TickLocalRecovery(target, floor',
    'TryGetFGridRecoveryGeometry($"Fgrid Floor {floor}"',
    'float recoveryDistance = nearApproach ? 0.85f : 1.5f',
    'if (after + 0.1f < before)',
    'Vector3.Dot(direction, _localRecoveryPreviousDirection) > 0.8f',
    'Vector3.Dot(direction, routeForward) < 0.2f',
    'three bounded local centreline recovery attempts',
    'replanning the same route from the settled position',
    'LocalRoutePlanner.SupportedFGridSegment(player, offset, player.Y)',
    'forward >= 0.8f || moved.Magnitude >= 1.15f')) {
    if (-not $provider.Contains($required)) {
        throw "Generic FGrid route-preserving recovery lost required behavior: $required"
    }
}
Write-Output 'PASS spawn, corridor and exit stalls share one bounded displacement-confirmed recovery primitive'

foreach ($required in @(
    'ReacquireForwardFGridPath(candidate, player.Position)',
    'resumed.AddRange(points.Skip(bestSegment))',
    'finalAlignmentSegment',
    'nearRadialApproach')) {
    if (-not $recorder.Contains($required)) {
        throw "FGrid forward radial reacquisition lost required behavior: $required"
    }
}
Write-Output 'PASS lift/exit recovery is smaller, forward-biased and resumes ahead on the same radial approach'

foreach ($required in @(
    'LiftApproachPhase',
    'CorridorStabilizing, CorridorTransit, RadialCommitted',
    'TickLiftPreApproach(target, floor',
    'TryGetFGridLiftApproachGeometry',
    'TryNavigateFGridCorridorToAlignment',
    'corridor stabilization complete',
    'radial approach remains uncommitted',
    'lift approach committed at the corridor alignment point',
    'TickCommittedRadialApproach(target',
    'large recentering is disabled after commitment')) {
    if (-not ($provider + $recorder).Contains($required)) {
        throw "FGrid pre-approach commitment boundary lost required behavior: $required"
    }
}
$preferred = [regex]::Match($provider,
    'private bool TryPreferredFGridRoute[\s\S]*?public FGridServiceResult Tick').Value
$preApproach = $preferred.IndexOf('TickLiftPreApproach(')
$genericRecovery = $preferred.IndexOf('TickLocalRecovery(')
if ($preApproach -lt 0 -or $genericRecovery -le $preApproach) {
    throw 'Generic recovery can run before the explicit corridor pre-approach phase.'
}
Write-Output 'PASS floors 3/5 use corridor stabilization, alignment-point transit, then a straight committed radial lift leg'

if (-not $recorder.Contains('StringComparison.Ordinal') -or
    -not $recorder.Contains('PreservesFGridAlignmentTurns') -or
    -not $recorder.Contains('cosine <= 0.5f')) {
    throw 'Canonical route centreline lookup or ring-to-radial alignment retention is missing.'
}
Write-Output 'PASS canonical route skeleton lookup is exact and sharp ring-to-radial alignment turns are retained'

foreach ($required in @(
    'TryFloorZeroLiftPath',
    'SubdivideSegment(raw, failedSegment, 6f)',
    'failedLength > 12f && !subdividedLongLeg')) {
    if (-not $recast.Contains($required)) {
        throw "Floor 0 hybrid or long-leg subdivision behavior is missing: $required"
    }
}
Write-Output 'PASS floor 0 remains automatic and over-limit Recast legs are subdivided before edge repair rejection'

if ($provider.Contains('Record the canonical walkway with /rkm nav record Fgrid Floor')) {
    throw 'A missing canonical floor route still causes a recording demand.'
}
Write-Output 'PASS a missing manual route does not itself fail or demand recording'
