$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$objective = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionObjective.cs') -Raw
$completion = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCompletionTracker.cs') -Raw
$vehicle = Get-Content -LiteralPath (Join-Path $root 'RKmission/VehicleLifecycle.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$scotty = Get-Content -LiteralPath (Join-Path $root 'RKmission/ScottyboiWarpProvider.cs') -Raw
function Assert($condition, $message) { if (-not $condition) { throw $message } }

Assert ($objective.Contains('message is ChatTextMessage chat ? chat.Text') -and
    $objective.Contains('message is FormatFeedbackMessage feedback ? feedback.Message')) `
    'Both supported live AO message forms must supply completion text.'
Assert ($objective.Contains('if (!Finale || !_steps.Any(x => x.Sent)) return;')) `
    'Completion text must remain ignored before an owned finale action.'
Assert ($completion.Contains('!(allObjectiveStepsProven || serverCompletion)') -and
    $completion.Contains('live.Any(x => x.Identity == missionId)') -and
    $completion.Contains('TimeSpan.FromSeconds(2)') -and
    $completion.Contains('deletedByUser')) `
    'Owned action, supported proof, exact quest absence, stable delay, and manual-delete guards must remain intact.'
Assert (-not $objective.Contains('received an item as mission reward') -and
    -not $objective.Contains('Received reward:')) `
    'Reward item or credit text must not become completion authority.'
foreach ($semantic in @('KillPersonAction', 'FindPersonAction', 'FindItemAction', 'Returning')) {
    Assert ($objective.Contains($semantic)) "Objective semantic guard missing: $semantic"
}
Write-Output 'PASS ChatText/FormatFeedback acknowledgement and completion safety invariants'

Assert ($vehicle.Contains('public bool FlightTransitionCombatGated') -and
    $vehicle.Contains('player.IsAttacking && !Game.IsZoning && !Playfield.IsDungeon') -and
    $vehicle.Contains('HasUsableFlightVehicle(player)')) `
    'Emergency eligibility must identify only an outdoor attacking gate with a usable vehicle.'
Assert ($bot.Contains('_vehicle.FlightTransitionCombatGated && _verifiedOutdoorMissionExit') -and
    $bot.Contains('if (flightRequired && vehicle == VehicleTransitionResult.Blocked')) `
    'Emergency extraction must require verified exit, real flight need, and exact combat-blocked result.'
Assert ($bot.Contains('_verifiedOutdoorMissionExit = !warpExitVerified && !Playfield.IsDungeon;')) `
    'Verified ordinary outdoor exit must be the emergency provenance gate.'
Assert ($bot.Contains('_emergencyExtractionDestination = _rollTerminalPlayfield') -and
    $bot.Contains('_warp.Tick(_emergencyExtractionDestination, null, true)') -and
    $bot.Contains('_warp.VerifiedDestination(_emergencyExtractionDestination)') -and
    $bot.Contains('normal autonomous travel planner resumed')) `
    'Extraction must use the saved safe hub, verified provider arrival, and planner resumption.'
Assert ($scotty.Contains('bool requireZoning = false') -and
    $scotty.Contains('if (!requireZoning && Playfield.ModelIdentity.Instance == targetId')) `
    'Emergency same-playfield extraction must bypass only the already-there shortcut and still require provider zoning.'
Assert ($bot.Contains('ResetEmergencyExtraction();') -and
    $bot.Contains('Scotty emergency extraction failed; automatic travel safely stopped')) `
    'Emergency state must reset through lifecycle cleanup and stop on bounded provider failure.'
$insideCombat = $bot.IndexOf('Combat resumed during the completed-mission warp wait; returning to dungeon exit handling.')
Assert ($insideCombat -ge 0) 'Inside-dungeon completed-mission combat cancellation must remain unchanged.'
Write-Output 'PASS narrow verified-exit combat extraction, bounded cleanup, and existing dungeon behavior'
