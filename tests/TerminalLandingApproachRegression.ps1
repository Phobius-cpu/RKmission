param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$catalog = Get-Content -LiteralPath (Join-Path $root 'RKmission/LogisticsRouteCatalog.cs') -Raw
$vehicle = Get-Content -LiteralPath (Join-Path $root 'RKmission/VehicleLifecycle.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$routes = Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/LogisticsRoutes.json') -Raw | ConvertFrom-Json

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

Assert ($catalog.Contains('terminalPosition.Y, terminalPosition.Z') -and
    -not $catalog.Contains('playerPosition.Y, terminalPosition.Z')) `
    'unsurveyed terminal standoff uses grounded live terminal Y rather than airborne player Y'
Assert ($catalog.Contains('const float landingRadius = 5f') -and
    $catalog.Contains('terminalPosition.Y + groundClearance') -and
    $catalog.Contains('TryFindTerminalLandingStaging')) `
    'landing staging is lateral, outside terminal collision body, and terminal-height-relative'
Assert ($vehicle.Contains('RequireFootForTerminal') -and
    $vehicle.Contains('flight ownership is intentionally irrelevant') -and
    $vehicle.Contains('_terminalDismountRequested') -and
    $vehicle.Contains('if (!_terminalDismountRequested)')) `
    'user-owned and RKMission-owned flight share a terminal-only one-shot safe exit path'
Assert ($vehicle.Contains('x.Id == YalmahaXl29478ItemId && x.IsEquipped') -and
    $vehicle.Contains('equippedVehicle.MoveToInventory();') -and
    $vehicle.Contains('automatic-inventory-slot=111') -and
    -not $vehicle.Contains('Enumerable.Range(64, 30)') -and
    $vehicle.Contains('Dynel.VehiclePointer is AOSharp''s persistent movement/position backing') -and
    -not $vehicle.Contains('player.VehiclePointer != IntPtr.Zero') -and
    $vehicle.Contains('terminal-vehicle-detachment-pending')) `
    'terminal rolling requires explicit Yalmaha unequip and live vehicle attachment clearance'
Assert ($bot.Contains('string vehicleFailure = _vehicle.Status;') -and
    $bot.Contains('could not be verified; {vehicleFailure}')) `
    'terminal timeout diagnostics are captured before Stop resets vehicle lifecycle state'
Assert ($vehicle.Contains('RequireFlightAfterMissionBatch') -and
    $vehicle.Contains('attached && player.MovementState == MovementState.Fly') -and
    $vehicle.Contains('batch-remount-equip-requested-once') -and
    $bot.Contains('_missionBatchRemountPending = true;') -and
    $bot.Contains('Mission batch remount required before mission selection/travel') -and
    $bot.Contains('Mission batch remount verified: equipped vehicle attachment')) `
    'completed rolling batch blocks mission selection and travel until mandatory remount is live-verified'
Assert ($vehicle.Contains('player.IsFalling || Item.HasPendingUse || Spell.HasPendingCast') -and
    $vehicle.Contains('TimeSpan.FromMilliseconds(750)') -and
    $vehicle.Contains('TimeSpan.FromSeconds(45)')) `
    'live non-Fly, non-falling, settled actions and bounded transition time gate terminal approach'
Assert ($vehicle.Contains('public bool RestoreOriginal()') -and
    $vehicle.Contains('if (!_ownsFlight || player == null || player.MovementState != MovementState.Fly) return true;')) `
    'non-terminal flight ownership restoration policy remains unchanged'
Assert ($bot.Contains('_returnTerminalPlanReady') -and
    $bot.Contains('Vector3 approach = _returnTerminalApproach;') -and
    $bot.Contains('Vector3 landingStaging = _returnLandingStaging;')) `
    'one grounded landing/approach plan is retained instead of chasing a repeatedly changing point'
Assert ($bot.Contains('Vector3.Distance(player.Position, terminal.Position) <= RollTerminalApproachDistance') -and
    $bot.Contains('foot != VehicleTransitionResult.Ready') -and
    $bot.IndexOf('foot != VehicleTransitionResult.Ready') -lt
        $bot.IndexOf('Vector3.Distance(player.Position, terminal.Position) <= RollTerminalApproachDistance',
            $bot.IndexOf('private bool ReturnToRollTerminal()'))) `
    'authoritative grounded foot verification precedes true 3D terminal-range arrival'
Assert ($bot.Contains('_returnNoProgressAttempts >= 4') -and
    $bot.Contains('Return to roller stopped after') -and
    $bot.Contains('player-to-standoff=') -and $bot.Contains('player-to-terminal=')) `
    'bounded on-foot no-progress watchdog and rich diagnostics remain present'

$oaAlternate = '(MissionTerminal:C007021C)'
$oaSurveyed = '(MissionTerminal:C001021C)'
Assert (-not ($routes.Routes.OriginTerminalIdentity -contains $oaAlternate) -and
    ($routes.Routes.OriginTerminalIdentity -contains $oaSurveyed)) `
    'fixture distinguishes unsurveyed OA C007021C from surveyed C001021C'
foreach ($site in @('oa', 'bor', 'icc')) {
    $siteRoutes = @($routes.Routes | Where-Object Site -eq $site)
    Assert (($siteRoutes.Purpose -contains 'bank') -and ($siteRoutes.Purpose -contains 'shop')) `
        "$site shared logistics hub retains bank/shop route reuse"
}

Assert ($bot.Contains('return CycleReadiness.IsReadyToRefill(BuildRollReadinessSnapshot(), out unresolved);') -and
    $bot.Contains('if (!ReadyToRoll(out string finalInvariant))') -and
    $bot.Contains('TerminalInstance = _rollingTerminalIdentity.Instance')) `
    'refill/final-roll semantics and exact issuing-terminal binding remain intact'
