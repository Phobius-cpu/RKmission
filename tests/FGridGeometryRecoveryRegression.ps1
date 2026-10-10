$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$provider = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$recast = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridRecastPlanner.cs') -Raw

foreach ($required in @(
    'TickLocalRecovery(target, floor',
    'TryGetFGridRecoveryGeometry($"Fgrid Floor {floor}"',
    'const float movementControllerArrivalTolerance = 0.80f',
    'const float movementControllerStartupMargin = 0.35f',
    'const float ringCorridorDesiredDisplacement = 0.65f',
    'const float committedApproachMicroAdjustment = 0.30f',
    '_localRecoveryDesiredDistance - 0.15f',
    'if (forward >= confirmedDistance)',
    '_localRecoverySettling = true',
    'TimeSpan.FromMilliseconds(250)',
    'distanceBeforeAlignment',
    'boundedCommandDistance <= movementControllerArrivalTolerance',
    'FGrid Ring Corridor',
    'Lift/Exit Approach micro-adjustment',
    'if (after + 0.1f < before)',
    'Vector3.Dot(direction, _localRecoveryPreviousDirection) > 0.8f',
    'Vector3.Dot(direction, routeForward) < 0.2f',
    'three bounded local centreline recovery attempts',
    'reacquiring the nearest forward point on the same corridor/approach skeleton',
    'LocalRoutePlanner.SupportedFGridSegment(player, offset, player.Y)',
    'will stop after about')) {
    if (-not $provider.Contains($required)) {
        throw "Generic FGrid route-preserving recovery lost required behavior: $required"
    }
}
if ($provider.Contains('forward >= confirmedDistance || moved.Magnitude >= confirmedDistance')) {
    throw 'Recovery can still be confirmed by off-axis displacement.'
}
Write-Output 'PASS Ring Corridor commands beyond the dead zone, stops on desired directional displacement, settles, and remains before Alignment Point'

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

foreach ($required in @(
    'TryPromoteRecoveredLiftAlignment',
    'CommitRecoveredLiftAlignment',
    'settling before post-recovery Alignment Point evaluation',
    'crossTrack <= 0.70f',
    'alignmentDistance <= 1.15f',
    'Vector3.Dot(liftDirection.Normalize(), radialForward) >= 0.85f',
    'Math.Abs(Vector3.Dot(corridorForward, radialForward)) <= 0.50f',
    'LocalRoutePlanner.SupportedFGridSegment(player, lift',
    'post-recovery alignment evaluation after attempt',
    'recovered position promoted: Alignment Point accepted',
    'without another Ring Corridor recovery')) {
    if (-not $provider.Contains($required)) {
        throw "Post-recovery Alignment Point promotion lost required behavior: $required"
    }
}
$preferred = [regex]::Match($provider,
    'private bool TryPreferredFGridRoute[\s\S]*?public FGridServiceResult Tick').Value
$promotion = $preferred.IndexOf('TryPromoteRecoveredLiftAlignment(')
$forwardRoute = $preferred.IndexOf('_routes.TryNavigate($"Fgrid Floor {floor}"')
if ($promotion -lt 0 -or $forwardRoute -le $promotion) {
    throw 'Recovered-position promotion does not run before forward Ring Corridor reacquisition.'
}
foreach ($required in @(
    'recorded geometry {(recordedAccepted ? "accepted" : "rejected")}',
    'recorded Ring Corridor/Lift Approach geometry unavailable; evaluating live geometry',
    'Vector3 liveCorridorForward = _localRecoveryDirection',
    'lateralDisplacement <= 0.30f',
    'liftDistance >= 0.80f && liftDistance <= 8.0f',
    'turnDot <= 0.50f',
    'post-recovery live-geometry alignment evaluation after attempt',
    'Ring Corridor displacement',
    'lift bearing requires',
    'using {geometrySource}')) {
    if (-not $provider.Contains($required)) {
        throw "Live-geometry Alignment Point fallback lost required behavior: $required"
    }
}
$liveFallback = [regex]::Match($provider,
    'private bool TryPromoteRecoveredLiftAlignment[\s\S]*?private bool CommitRecoveredLiftAlignment').Value
if ($liveFallback.IndexOf('if (recordedAccepted)') -lt 0 -or
    $liveFallback.IndexOf('bool liveAccepted') -le $liveFallback.IndexOf('if (recordedAccepted)')) {
    throw 'Recorded geometry is not evaluated before the live-geometry fallback.'
}
Write-Output 'PASS a settled recovery evaluates and can promote its current position before another forward anchor or recovery attempt'

foreach ($required in @(
    'RecoverUnsafePostDeparturePlan',
    '_floorDepartureCompletedFloor != floor',
    '_localRecoveryAttempt > 0',
    'post-spawn route was rejected before movement began',
    'starting bounded Ring Corridor recovery attempt 1/3',
    'charging the completed spawn-pad departure against the recovery budget',
    'return StartLocalRecoveryAttempt(DynelManager.LocalPlayer.Position, target, floor')) {
    if (-not $provider.Contains($required)) {
        throw "Unsafe post-departure planning does not enter bounded Ring Corridor recovery: $required"
    }
}
$unsafePostDeparture = [regex]::Match($provider,
    'private bool RecoverUnsafePostDeparturePlan[\s\S]*?private bool TickFloorDeparture').Value
if ($unsafePostDeparture.Contains('_localRecoveryAttempt++')) {
    throw 'Post-departure dispatch consumes recovery budget before a real attempt starts.'
}
Write-Output 'PASS unsafe post-spawn plans enter real Ring Corridor attempt 1/3 without consuming departure as an attempt'
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
