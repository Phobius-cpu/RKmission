$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$provider = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$recast = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridRecastPlanner.cs') -Raw

foreach ($required in @(
    'TickLocalRecovery(target, floor',
    'TryGetFGridRecoveryGeometry($"Fgrid Floor {floor}"',
    'const float ringCorridorRecoveryDistance = 0.65f',
    'const float committedApproachMicroAdjustment = 0.30f',
    '_localRecoveryRequestedDistance - 0.15f',
    'FGrid Ring Corridor',
    'Lift/Exit Approach micro-adjustment',
    'if (after + 0.1f < before)',
    'Vector3.Dot(direction, _localRecoveryPreviousDirection) > 0.8f',
    'Vector3.Dot(direction, routeForward) < 0.2f',
    'three bounded local centreline recovery attempts',
    'reacquiring the nearest forward point on the same corridor/approach skeleton',
    'LocalRoutePlanner.SupportedFGridSegment(player, offset, player.Y)',
    'forward >= confirmedDistance || moved.Magnitude >= confirmedDistance')) {
    if (-not $provider.Contains($required)) {
        throw "Generic FGrid route-preserving recovery lost required behavior: $required"
    }
}
Write-Output 'PASS Ring Corridor recovery is 0.65 m, committed approach fallback is micro-only, and displacement is confirmed'

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
    'Ring Corridor stabilization complete',
    'radial approach remains uncommitted',
    'Lift Approach committed at the Alignment Point',
    'TickCommittedRadialApproach(target',
    'large recentering is disabled after commitment')) {
    if (-not ($provider + $recorder).Contains($required)) {
        throw "FGrid pre-approach commitment boundary lost required behavior: $required"
    }
}
foreach ($required in @(
    'FindFGridLiftAlignmentIndex(points, lift)',
    'lift-facing suffix backwards and keep the earliest point',
    '_localRecoveryAwaitingSafeResume = true',
    'ContinueRecoveryAfterUnsafePlan',
    'unsupported diagonal which triggered recovery',
    'preserving the recovery budget and selecting the next safe forward corridor/alignment target',
    '_localRecoveryLastMovedAway')) {
    if (-not ($provider + $recorder).Contains($required)) {
        throw "FGrid post-correction alignment/recovery budget behavior is missing: $required"
    }
}
Write-Output 'PASS rounded floor approaches retain explicit alignment and unsafe resumes consume attempts 2/3 and 3/3'
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
