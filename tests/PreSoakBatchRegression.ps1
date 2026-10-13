$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read([string]$path) { Get-Content -LiteralPath (Join-Path $root $path) -Raw }

$bot = Read 'RKmission/RkMissionBot.cs'
$neko = Read 'RKmission/MissionEntranceResolver.cs'
$hostUi = Read 'RKmission/Plugins/MaliMissionRoller2/MainWindow.cs'
$nekoXml = Read 'RKmission/Plugins/MaliMissionRoller2/UI/Views/NekoView.xml'
$death = Read 'RKmission/DeathRecoveryController.cs'
$routes = Read 'RKmission/NavigationRouteRecorder.cs'
$help = Read 'RKmission/Plugins/MaliMissionRoller2/Views/HelpWindow.cs'
$vehicle = Read 'RKmission/VehicleLifecycle.cs'

$long = $bot.IndexOf('TravelResult longResult = _longTravel.Tick(_selected.PlayfieldId, _selected.Entrance);')
$select = $bot.IndexOf('_entranceResolver.Select(live);', $long)
$local = $bot.IndexOf('_travel.Tick(_selected', $select)
if ($long -lt 0 -or $select -le $long -or $local -le $select) { throw 'Expected CrossPlayfield -> NekoEntry -> LocalMissionTravel ordering.' }
foreach ($required in @('_travel.Reset(); _movement.Halt(MovementOwner.MissionEntrance)', 'AcceptedOrZoning', 'KeyAccepted', 'VerifyCurrentDungeon')) {
    if (-not ($bot + $neko).Contains($required)) { throw "Neko handoff invariant missing: $required" }
}
Write-Output 'PASS destination-local Neko runs before physical travel and stale local movement is cancelled'

foreach ($required in @('HostedTab.Neko', 'ShowNekoTab', 'AttachNekoView', 'NekoWarp', 'NekoHelp', 'NekoToggle', 'NekoDiagnostics')) {
    if (-not ($hostUi + $nekoXml).Contains($required)) { throw "Neko UI wiring missing: $required" }
}
if (-not $nekoXml.Contains('label="Toggle Key"')) { throw 'Toggle Key control missing.' }
Write-Output 'PASS shared Neko tab exposes warp/help/toggle and live diagnostics'

foreach ($required in @('IsRubiKaPlayfield(_reclaimPlayfield)', 'Resurrection Sickness Removal', 'TemporarySkillReduction', 'Phase = "Readiness"')) {
    if (-not $death.Contains($required)) { throw "Death recovery gate missing: $required" }
}
$rkGate = $death.IndexOf('IsRubiKaPlayfield(_reclaimPlayfield)')
$sickness = $death.IndexOf('TemporarySkillReduction', $rkGate)
$readiness = $death.IndexOf('_readiness.Start()', $sickness)
if ($rkGate -lt 0 -or $sickness -le $rkGate -or $readiness -le $sickness) { throw 'Death recovery order is not RK guard -> sickness -> readiness.' }
foreach ($required in @('_deathReturnToRollTerminal = true', 'ReturnToRollTerminal()', 'exact saved roller terminal')) {
    if (-not $bot.Contains($required)) { throw "Saved-city recovery invariant missing: $required" }
}
if (-not $help.Contains('saved/reclaim on Rubi-Ka')) { throw 'Help lacks prominent Rubi-Ka reclaim prerequisite.' }
Write-Output 'PASS RK-only reclaim, Nano Can-or-wait, readiness, Scotty/saved-terminal resume are ordered'

foreach ($required in @('MovementMode', 'AltitudeProfile', 'Directionality', 'RouteModeMatches', 'CanReacquireRoute', 'RouteDirection(route) != "Forward"')) {
    if (-not $routes.Contains($required)) { throw "Route metadata/profile support missing: $required" }
}
if (-not $routes.Contains('string.IsNullOrWhiteSpace(route?.MovementMode) ? "Foot"')) { throw 'Legacy route files do not default to Foot.' }
if (-not $routes.Contains('string.IsNullOrWhiteSpace(route?.Directionality) ? "Bidirectional"')) { throw 'Legacy route files do not default to Bidirectional.' }
Write-Output 'PASS route metadata is backward compatible and supports Foot/Vehicle selection plus forward reacquisition'

foreach ($required in @('ObserveOriginal()', '_ownsFlight', 'SafeToTransition', 'Game.IsZoning', 'IsAttacking', 'MovementOwner.OutdoorTravel', 'activation-unverified', 'RestoreOriginal()')) {
    if (-not ($vehicle + $bot).Contains($required)) { throw "Vehicle ownership/transition invariant missing: $required" }
}
if (-not $bot.Contains('localDistance >= 120f')) { throw 'Automatic flight is not demand-driven by route cost.' }
Write-Output 'PASS vehicle state is demand-driven, live-verified, transition-gated, and restored only when RKMission owns it'
