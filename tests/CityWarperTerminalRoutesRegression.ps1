$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$catalog = Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/NavigationRoutes.json') -Raw | ConvertFrom-Json
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw

function Assert($condition, $message) {
    if (-not $condition) { throw $message }
}

$expected = @(
    @{ Name = 'OA Sirocco to Terminal - Flying'; Pf = 540; Count = 124 },
    @{ Name = 'OA Clan Agency to Terminal - Flying'; Pf = 540; Count = 43 },
    @{ Name = 'ICC Lift to Terminal'; Pf = 655; Count = 53 },
    @{ Name = 'ICC Statue to Terminal'; Pf = 655; Count = 27 },
    @{ Name = 'Bor Baffbots to Terminal'; Pf = 800; Count = 35 }
)

foreach ($item in $expected) {
    $matches = @($catalog.Routes | Where-Object Name -eq $item.Name)
    Assert ($matches.Count -eq 1) "Missing or duplicate bundled route: $($item.Name)"
    $route = $matches[0]
    Assert ($route.Playfield -eq $item.Pf) "Wrong playfield for $($item.Name)"
    Assert ($route.Points.Count -eq $item.Count) "Recorded points changed for $($item.Name)"
    Assert ($route.MovementMode -eq 'Auto') "MovementMode changed for $($item.Name)"
    Assert ($route.AltitudeProfile -eq 'Ground') "AltitudeProfile changed for $($item.Name)"
    Assert ($route.Directionality -eq 'Bidirectional') "Directionality changed for $($item.Name)"
}

# Existing bundled terminal-to-grid routes must remain intact.
foreach ($name in @('Terminal to Fgrid OA', 'Terminal to grid ICC', 'Terminal to grid Bor')) {
    Assert (@($catalog.Routes | Where-Object Name -eq $name).Count -eq 1) "Existing route lost: $name"
}

Assert ($recorder.Contains('IsCityReturnRoute(x)') -and
        $recorder.Contains('x.Playfield == Playfield.ModelIdentity.Instance')) `
    'City-return selection is not filtered by the observed playfield.'
Assert ($recorder.Contains('targetDistance > 6f') -and
        $recorder.Contains('horizontalDistance > 12f') -and
        $recorder.Contains('verticalDistance > 6f')) `
    'Endpoint/corridor safety gates are missing.'
Assert ($recorder.Contains('resumed.AddRange(points.Skip(segment))')) `
    'Forward-only corridor reacquisition is missing.'
Assert ($recorder.Contains('if (best.Playfield != 655)') -and
        $recorder.Contains('bestPath = SimplifyRecordedPath(bestPath)')) `
    'ICC vertical samples are no longer explicitly protected from smoothing.'
Assert ($recorder.Contains('RouteDirection(route) == "Forward"')) `
    'Route directionality is not enforced.'
Assert ($recorder.Contains('RouteModeMatches(x, player.MovementState == MovementState.Fly)')) `
    'Existing Foot/Vehicle/Auto semantics are not used.'

Assert ($bot.Contains('string.Equals(_longTravel.CurrentProvider, "Scottyboi"')) `
    'City-return recordings are not gated to verified Scotty travel.'
Assert ($bot.Contains('CityReturnRouteResult.NoMatch') -and
        $bot.Contains('using existing local terminal-return behavior')) `
    'Safe no-match fallback is missing.'
Assert ($bot.Contains('Exact saved roller terminal verification passed') -and
        $bot.Contains('x.Identity.Type == IdentityType.MissionTerminal') -and
        $bot.Contains('Vector3.Distance(x.Position, _rollTerminalPosition) <= 1f')) `
    'Exact terminal identity/position verification is no longer the final gate.'

# A midpoint observation is intentionally a forward reacquisition case, not a
# requirement to return to the first sample.
$oa = $catalog.Routes | Where-Object Name -eq 'OA Sirocco to Terminal - Flying'
$mid = [int][Math]::Floor($oa.Points.Count / 2)
Assert ($mid -gt 1 -and $mid -lt ($oa.Points.Count - 1)) 'Invalid forward reacquisition fixture.'
Assert (($oa.Points[$mid][0] -ne $oa.Points[0][0]) -or
        ($oa.Points[$mid][2] -ne $oa.Points[0][2])) 'Route fixture has no forward corridor progress.'

Write-Output 'PASS OA/ICC/Borealis Scotty-to-terminal routes, metadata, filtering, forward reacquisition, fallback and exact terminal gate'
