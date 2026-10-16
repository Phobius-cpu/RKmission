$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$provider = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw

foreach ($required in @(
    'PortalApproachPoint(_exit.Position, _route.Floor)',
    'FGridGeometry.CenterX - portal.X',
    'TryPreferredFGridRoute(approach, _route.Floor, true',
    'useRange <= 1.55f && floorDelta <= 2.0f',
    'MovementAction.FullStop',
    'Quaternion.LookRotation(facing, Vector3.Up)',
    'TimeSpan.FromMilliseconds(600)',
    '_portalUseAttempts < 3',
    'Use dispatched attempt {_portalUseAttempts}/3')) {
    if (-not $provider.Contains($required)) {
        throw "FGrid walkable-side portal approach lost required behavior: $required"
    }
}
Write-Output 'PASS FGrid approaches verified exits from the walkable side, settles, faces, and uses with a bounded retry budget'

foreach ($required in @(
    'Unable to get within interaction range of FGrid portal',
    'within interaction range, but no zoning started',
    'live portal identity/position did not match selected verified portal',
    'Fixer Grid exit reached unexpected playfield',
    'FGrid zoning ended in playfield')) {
    if (-not $provider.Contains($required)) {
        throw "FGrid portal outcome diagnostics lost required distinction: $required"
    }
}
if ($provider.Contains('else if (DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(3))')) {
    throw 'FGrid portal interaction regressed to blind unbounded Use dispatch.'
}
Write-Output 'PASS FGrid diagnostics distinguish approach, Use/no-zone, portal mismatch, and wrong destination outcomes'
