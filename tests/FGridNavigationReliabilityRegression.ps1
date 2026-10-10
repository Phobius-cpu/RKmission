$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$provider = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw

foreach ($required in @(
    'CompatibleSharpNavArtifact()',
    'StringComparison.OrdinalIgnoreCase',
    '".nav"',
    '".navmesh"',
    'SMovementController.NavAgent?.HasPathfinder == true')) {
    if (-not $provider.Contains($required)) {
        throw "FGrid compatible artifact/pathfinder resolution lost required behavior: $required"
    }
}
if ($provider.Contains('NavMeshes/4107.nav is missing') -or
    $provider.Contains('4107.nav is not loaded by AO#')) {
    throw 'FGrid SharpNav is still rejected by the legacy single-filename gate.'
}
Write-Output 'PASS FGrid accepts case-insensitive .nav/.Navmesh candidates and trusts the loaded AOSharp pathfinder'

$fallback = [regex]::Match($provider,
    'private bool TryNavigateFGrid[\s\S]*?private string DescribeSharpNavArtifact').Value
$recast = $fallback.IndexOf('TryNavigateRecast(')
$sharp = $fallback.IndexOf('HasPathfinder')
$corridor = $fallback.IndexOf('TryFGridGroundCost(')
if ($recast -lt 0 -or $sharp -le $recast -or $corridor -le $sharp) {
    throw 'FGrid fallback order is not Recast then loaded SharpNav corridor validation.'
}
foreach ($required in @(
    'Recast waypoint movement stalled',
    '_recastRejectedTarget = target',
    'mesh movement made no progress for nine seconds',
    '_meshRejectedDestination = target')) {
    if (-not $provider.Contains($required)) {
        throw "FGrid bounded rejection state lost required behavior: $required"
    }
}
Write-Output 'PASS stalled/rejected Recast falls through to bounded SharpNav without blind walking'

foreach ($required in @(
    'TryPreferredFGridRoute(lift, floor, false',
    'TryPreferredFGridRoute(_exit.Position, _route.Floor, true',
    '$"Fgrid Floor {floor}"',
    '$"fgrid-floor-{floor}-lift"',
    '$"fgrid-floor-{floor}-portal-{_exit.Identity.Instance}"',
    '_routes.TryNavigate(legacyRouteName, target')) {
    if (-not $provider.Contains($required)) {
        throw "FGrid named recorded fallback lost required behavior: $required"
    }
}
foreach ($required in @(
    'string.Equals(x.Name, routeName, StringComparison.OrdinalIgnoreCase)',
    'Vector3.Distance(player.Position, first) <= endpointTolerance',
    'Vector3.Distance(target, last) <= endpointTolerance',
    'SupportedRecordedFGridRoute')) {
    if (-not $recorder.Contains($required)) {
        throw "Recorded fallback endpoint verification lost required behavior: $required"
    }
}
Write-Output 'PASS canonical floor routes are preferred while legacy named fallbacks retain verified endpoints'

$planner = Get-Content -LiteralPath (Join-Path $root 'RKmission/RubiKaTravelPlanner.cs') -Raw
if (-not $planner.Contains('current == (int)PlayfieldId.FixerGrid') -or
    -not $planner.Contains('Normal provider fallback is paused while still inside Fixer Grid.')) {
    throw 'An internal FGrid failure can still fall through to PlayfieldGraph inside PF 4107.'
}
foreach ($required in @(
    'NormalizeUserRoutes(file.Routes)',
    '"Fgrid Floor 10a"',
    'OrderByDescending(x => x.route.RecordedAtUtc)',
    'ThenByDescending(x => x.index)')) {
    if (-not $recorder.Contains($required)) {
        throw "Recorded route canonicalization lost required behavior: $required"
    }
}
Write-Output 'PASS PF 4107 blocks unsafe graph fallback and user routes normalize deterministically'
