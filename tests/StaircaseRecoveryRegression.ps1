$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw

$begin = $dungeon.IndexOf('private bool TryBeginStaircaseRecovery')
$tick = $dungeon.IndexOf('private bool TickStaircaseRecovery', $begin)
$beginBody = $dungeon.Substring($begin, $tick - $begin)
$fullStop = $beginBody.IndexOf('MovementAction.FullStop')
$jumpStart = $beginBody.IndexOf('MovementAction.JumpStart')
if ($begin -lt 0 -or $fullStop -lt 0 -or $jumpStart -lt $fullStop -or
    $beginBody.Contains('MovementAction.ForwardStart')) {
    throw 'Staircase recovery no longer starts with a full stop and stationary jump.'
}
Write-Output 'PASS staircase recovery starts FullStop then JumpStart without ForwardStart'

$finish = $dungeon.IndexOf('private void FinishStaircaseRecovery', $tick)
$tickBody = $dungeon.Substring($tick, $finish - $tick)
$airborneGate = $tickBody.IndexOf('verticalLift >= StaircaseAirborneLift')
$jumpStop = $tickBody.IndexOf('MovementAction.JumpStop', $airborneGate)
$forwardStart = $tickBody.IndexOf('MovementAction.ForwardStart', $airborneGate)
if ($airborneGate -lt 0 -or $jumpStop -lt $airborneGate -or
    $forwardStart -lt $jumpStop -or
    -not $dungeon.Contains('StaircaseAirborneLift = 0.15f')) {
    throw 'Forward input is no longer gated on observed airborne lift after releasing jump.'
}
Write-Output 'PASS observed vertical lift gates JumpStop then ForwardStart'

if (-not $dungeon.Contains('TimeSpan.FromMilliseconds(1250)') -or
    -not $dungeon.Contains('TimeSpan.FromMilliseconds(900)') -or
    -not $dungeon.Contains('TimeSpan.FromSeconds(2)') -or
    -not $dungeon.Contains('MaxStaircaseRecoveryAttempts = 2') -or
    -not $dungeon.Contains('staircase recovery exhausted after')) {
    throw 'Staircase phase deadlines or the two-attempt failure bound regressed.'
}
Write-Output 'PASS airborne, forward, settle, and attempt bounds remain finite'

if (-not $dungeon.Contains('verticalLift >= 0.45f && forwardProgress >= 0.35f') -or
    -not $dungeon.Contains('Rebuilding movement from the observed landing position.') -or
    -not $dungeon.Contains('recovery.LastProgress = recovery.WaypointStarted = DateTime.UtcNow')) {
    throw 'Successful staircase recovery no longer requires tangible vertical/horizontal progress and settle continuation.'
}
Write-Output 'PASS success requires tangible displacement before route continuation'

$stopInputs = $dungeon.IndexOf('private void StopStaircaseInputs')
$cancel = $dungeon.IndexOf('private void CancelStaircaseRecovery', $stopInputs)
$cleanupBody = $dungeon.Substring($stopInputs, $cancel - $stopInputs)
if (-not $cleanupBody.Contains('MovementAction.JumpStop') -or
    -not $cleanupBody.Contains('MovementAction.ForwardStop') -or
    -not $cleanupBody.Contains('MovementAction.FullStop') -or
    -not $tickBody.Contains('MovementArbiter.Current.Owner != MovementOwner.CombatPosition') -or
    -not $tickBody.Contains('!player.IsAlive || Game.IsZoning') -or
    -not $bot.Contains('_dungeon?.CancelMovementRecovery();')) {
    throw 'Held-input cleanup no longer covers stop, death, zoning, or ownership change.'
}
Write-Output 'PASS held inputs are released on stop, death, zoning, and ownership change'

if (-not $beginBody.Contains('MovementArbiter.Current.Owner != MovementOwner.CombatPosition') -or
    -not $beginBody.Contains('enemyLift < CombatTopologyVerticalSeparation') -or
    -not $beginBody.Contains('routeLift < StaircaseAirborneLift') -or
    -not $beginBody.Contains('recovery.Waypoints.Skip(recovery.WaypointIndex)') -or
    -not $dungeon.Contains('if (TickStaircaseRecovery()) return;')) {
    throw 'Recovery escaped its upward CombatPosition-only ownership boundary.'
}
Write-Output 'PASS recovery remains scoped to upward CombatPosition stalls'

$combatTick = $dungeon.IndexOf('private bool TickCombatRecovery')
$combatFail = $dungeon.IndexOf('private void FailCombatRecoveryRoute', $combatTick)
$combatBody = $dungeon.Substring($combatTick, $combatFail - $combatTick)
$stall = $combatBody.IndexOf('now - recovery.LastProgress > TimeSpan.FromSeconds(7)')
$directRecovery = $combatBody.IndexOf('TryBeginStaircaseRecovery(recovery, point)', $stall)
$recordedFallback = $combatBody.IndexOf('TryStartRecordedCombatTopology(room, enemy, recovery, position)', $stall)
$failureFallback = $combatBody.IndexOf('FailCombatRecoveryRoute(recovery, "no movement progress")', $stall)
if ($stall -lt 0 -or $directRecovery -lt $stall -or
    $recordedFallback -lt $directRecovery -or $failureFallback -lt $recordedFallback) {
    throw 'Generated same-room transit stalls no longer try staircase recovery before topology/failure fallbacks.'
}
Write-Output 'PASS genuine seven-second transit stalls directly invoke staircase recovery before failure'

if (-not $beginBody.Contains('float enemyLift = recovery.EnemyPosition.Y - player.Position.Y') -or
    -not $beginBody.Contains('float routeLift = recovery.Waypoints.Skip(recovery.WaypointIndex)')) {
    throw 'Staircase eligibility no longer recognizes a low first step on an upward enemy route.'
}
$playerY = 8.645
$firstWaypointY = 9.115
$enemyY = 17.10876
if (($firstWaypointY - $playerY) -ge 1.5 -or
    ($firstWaypointY - $playerY) -lt 0.15 -or
    ($enemyY - $playerY) -lt 1.5) {
    throw 'The confirmed room-7 elevations no longer exercise low-first-step upward-route eligibility.'
}
Write-Output 'PASS player Y=8.645, first waypoint Y=9.115, enemy Y=17.10876 satisfies upward-route eligibility'

foreach ($required in @(
    'TryGetProvenStaircaseDirection(recovery, player.Position,',
    'routeLift < StaircaseAirborneLift && !provenGeometry',
    'reuseProvenHeading ? provenDirection : target - player.Position',
    'heading={(reuseProvenHeading ? "proven geometry" : "current route")}',
    'Combat staircase recovery recorded proven geometry',
    'ProvenStaircaseReuseRadius = 6f',
    'ProvenStaircaseLifetime = TimeSpan.FromMinutes(5)',
    'ResetProvenStaircase();')) {
    if (-not $dungeon.Contains($required)) {
        throw "Proven staircase geometry reuse regressed: $required"
    }
}
Write-Output 'PASS a successful climb provides bounded same-room heading evidence for flat rebuilt routes'

foreach ($required in @(
    'Combat staircase recovery rejected: phase=',
    'Combat staircase recovery rejected: enemy lift=',
    'Combat staircase recovery rejected: attempt limit',
    'enemy lift={enemyLift:0.00}m, route lift={routeLift:0.00}m')) {
    if (-not $dungeon.Contains($required)) {
        throw "Staircase eligibility diagnostics regressed: $required"
    }
}
Write-Output 'PASS phase, ownership, elevation, proof, and attempt rejection diagnostics are explicit'

$departure = $combatBody.IndexOf('Room next = NextRoom(room);', $stall)
$provenRetry = $combatBody.IndexOf('TryGetProvenStaircaseDirection(recovery, position, out Vector3 retryDirection)', $stall)
$suppressed = $combatBody.IndexOf('room departure suppressed.', $provenRetry)
if ($provenRetry -lt 0 -or $suppressed -lt $provenRetry -or $departure -lt $suppressed -or
    -not $combatBody.Contains('_staircaseRecoveryAttempts < MaxStaircaseRecoveryAttempts')) {
    throw 'Room deferral can again bypass an available proven-geometry staircase retry.'
}
Write-Output 'PASS room departure is suppressed while a proven safe staircase retry remains'

$exhaustion = $dungeon.IndexOf('staircase recovery exhausted after')
$exhaustionEnd = $dungeon.IndexOf('_say($"Combat staircase recovery', $exhaustion)
$exhaustionBody = $dungeon.Substring($exhaustion, $exhaustionEnd - $exhaustion)
if ($exhaustionBody.Contains('ResetStaircaseRecoveryAttempts()')) {
    throw 'Exhaustion resets its own counter and can exceed the strict two-attempt bound.'
}
Write-Output 'PASS exhausted staircase recovery cannot reset itself into additional attempts'
