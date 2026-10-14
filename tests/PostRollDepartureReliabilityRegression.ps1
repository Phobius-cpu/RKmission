$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$vehicle = Get-Content -LiteralPath (Join-Path $root 'RKmission/VehicleLifecycle.cs') -Raw
$routes = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$fgrid = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw
$planner = Get-Content -LiteralPath (Join-Path $root 'RKmission/RubiKaTravelPlanner.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert (($bot -split 'Inventory.NumFreeSlots < _inventory.MinimumFreeSlots').Count -ge 3) `
    'rolling completion, capacity suspension, and departure must share MinimumFreeSlots'
Assert ($bot.Contains('_missionBatchRemountPending = _autoCycle && _autoAcceptedCount > 0;')) `
    'explicit /rkm auto restart with accepted missions must arm departure verification'
Assert ($bot.Contains('_longTravel.PrefersFootDeparture') -and
    $bot.Contains('_vehicle.RequireFlightAfterMissionBatch(false)') -and
    $bot.Contains('_vehicle.RequireFlightAfterMissionBatch();')) `
    'departure must choose verified Foot cooperation or live-proven vehicle flight'
Assert ($vehicle.Contains('batch-departure-foot-route-verified') -and
    $vehicle.Contains('attachedForFootRoute') -and
    $vehicle.Contains('attached && player.MovementState == MovementState.Fly')) `
    'both Foot and Vehicle departure modes require authoritative live state'
Assert ($vehicle.Contains('vehicle.Equip(EquipSlot.Weap_Hud1);') -and
    $vehicle.Contains('vehicle.EquipSlots.Contains(EquipSlot.Weap_Hud1)') -and
    $vehicle.Contains('vehicle.Slot.Type != IdentityType.Inventory') -and
    $vehicle.Contains('batch-remount-hud1-occupied') -and
    $vehicle.IndexOf('vehicle.Equip(EquipSlot.Weap_Hud1);') -gt
        $vehicle.IndexOf('public VehicleTransitionResult RequireFlightAfterMissionBatch(bool requireFlight)')) `
    'known main-inventory Yalmaha uses the validated one-shot AOSharp HUD1 equipment primitive'
Assert ($vehicle.Contains('dispatch=void-no-synchronous-ack') -and
    $vehicle.Contains('EquippedVehicleSnapshot()') -and
    $vehicle.Contains('pendingUse={Item.HasPendingUse}, pendingCast={Spell.HasPendingCast}')) `
    'equipment request and timeout retain source, target, acknowledgement, live equipment, and pending-action evidence'
Assert ($bot.Contains('string remountFailure = _vehicle.Status;') -and
    $bot.IndexOf('string remountFailure = _vehicle.Status;') -lt
        $bot.IndexOf('Say("Mission batch remount stopped: " + remountFailure')) `
    'post-batch remount failure is captured before Stop resets vehicle status'
Assert ($planner.Contains('PrefersFootDeparture') -and $fgrid.Contains('HasFootApproachRoute') -and
    $routes.Contains('HasFootRouteTo')) `
    'provider choice must retain an endpoint-compatible Foot terminal recording'

Assert ($routes.Contains('_issuedPlayIndex != _playIndex') -and
    $routes.Contains('movement.SetDestination(owner, waypoint)')) `
    'every active recorded-route waypoint change must submit the new destination'
Assert ($routes.Contains('Recorded route progress:') -and
    $routes.Contains('stuck-recoveries=') -and $routes.Contains('TimeSpan.FromSeconds(8)')) `
    'recorded playback must expose progress diagnostics and bounded no-progress detection'
Assert ($routes.Contains('no meaningful displacement after three bounded waypoint resets') -and
    $routes.Contains('StopPlayback(false)')) `
    'stale steering must terminate safely after bounded recovery'
Assert ($fgrid.Contains('Recorded Grid-terminal approach stopped safely:') -and
    $fgrid.IndexOf('Recorded Grid-terminal approach stopped safely:') -lt
        $fgrid.IndexOf('_movement.SetDestination(MovementOwner.FGridTravel, _terminalPosition);')) `
    'a failed recorded approach must not degrade to an unsafe direct shortcut'
Assert ($routes.Contains('SimplifyRecordedPath(candidate)') -and
    $routes.Contains('RecordedShortcutSafe')) `
    'terminal route smoothing remains corridor constrained and recordings remain intact'

Write-Output 'PASS post-roll departure mode and recorded-route progress recovery are source-guarded'
