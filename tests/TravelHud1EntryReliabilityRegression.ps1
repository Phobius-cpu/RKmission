$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$bot=Get-Content (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$travel=Get-Content (Join-Path $root 'RKmission/LocalMissionTravel.cs') -Raw
$vehicle=Get-Content (Join-Path $root 'RKmission/VehicleLifecycle.cs') -Raw
$flight=Get-Content (Join-Path $root 'RKmission/PostZoneFlightSafety.cs') -Raw
function Assert($ok,$why){if(-not $ok){throw $why}}

$preflight=$bot.IndexOf('bool flightRequired = _travel.Mode == TravelMode.Flying')
$select=$bot.IndexOf('_travel.SelectNearest(new[] { _selected })', $preflight)
Assert ($preflight -ge 0 -and $select -gt $preflight) 'Long local Fly preflight must run before committing/recommitting the route.'
Assert ($bot.Contains('localDistance >= 120f') -and $bot.Contains('vehicle != VehicleTransitionResult.Ready') -and
    $bot.Contains('MovementState != MovementState.Fly')) 'Long auto routes must hold for both transition success and authoritative live Fly state.'
Assert ($bot.Contains('!inFixerGrid') -and $bot.Contains('_travel.Mode == TravelMode.Auto')) 'FGrid and explicit travel-mode semantics must remain separate from local auto-flight preflight.'
Assert ($bot.Contains('!_travel.RouteUsesFlight') -and $travel.Contains('public bool RouteUsesFlight')) 'A Run estimate must be invalidated and rebuilt after live flight activation.'
Write-Output 'PASS long local mission routes preflight live Fly before committing movement and refresh stale Run estimates'

Assert ($vehicle.Contains('Manual flying HUD1 vehicle verified from the live slot') -and
    $vehicle.Contains('_verifiedVehicle?.Matches(current) == true')) 'A manual known/live flying HUD1 vehicle must enter the indoor transition path.'
Assert ($vehicle.Contains('indoor/live-equipped-and-grounded-verified')) 'Already-correct grounded indoor HUD1 must pass without churn.'
Assert ($vehicle.Contains('candidates.Count == 1') -and $vehicle.Contains('candidates.Count > 1') -and
    $vehicle.Contains('indoor/no-candidate-vehicle-removal-requested') -and
    $vehicle.Contains('indoor/ambiguous-vehicle-removal-requested')) 'Indoor selection must be unique or safely remove/hold without guessing.'
Assert ($bot.Contains('if (indoorHud1 != VehicleTransitionResult.Ready)')) 'Waiting, blocked and unavailable outcomes must not start dungeon work.'
Assert ($vehicle.Contains('without claiming stop-time ownership')) 'Manual vehicle ownership must not be converted into RKMission stop-time ownership.'
Write-Output 'PASS manual vehicle, known indoor item, unavailable/blocked/pending, correct gear and ambiguous candidates are gated safely'

foreach($required in @('HorizontalDeparture = 8f','ObserveExpectedOutdoorExit(_selected.Entrance)',
    '_landing - _zoningOutdoorExit.Value','candidate = new Vector3(from.X + direction.X * HorizontalDeparture',
    'from.Y, from.Z + direction.Z * HorizontalDeparture','progress >= 5f','player.Position.Y + 36f',
    'LocalRoutePlanner.ClearSegment','bounded geometry-safe recovery was exhausted')) {
    Assert ($flight.Contains($required) -or $bot.Contains($required)) "Post-zone clearance behavior missing: $required"
}
Assert (-not $flight.Contains('p.X + 18f') -and -not $flight.Contains('p.Z + 12f')) 'Fixed world-space post-zone diagonal must not return.'
foreach($pf in @('playfield == 540','playfield == 655','playfield == 800')) { Assert ($flight.Contains($pf)) "City exception lost: $pf" }
Write-Output 'PASS verified exit direction drives constant-altitude clearance before bounded geometry-safe diagonal climb; city exceptions remain'
