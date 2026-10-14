$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$objective = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionObjective.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

foreach ($required in @(
    'public bool HasFindPersonTarget',
    'public Identity FindPersonTarget',
    'step.Action is FindPersonAction && (!RewardConfirmed || !FinalActionsAllowed)',
    'FindPerson observation started for exact target',
    'FindPerson observation reset: exact target',
    'FindPerson observation complete for exact target',
    'FindPersonObservationConfirmation = TimeSpan.FromSeconds(5)',
    'now - step.ObservationStarted >= FindPersonObservationConfirmation',
    'awaiting mission acknowledgement',
    'FindPerson completion release: exact target')) {
    if (-not $objective.Contains($required)) {
        throw "FindPerson identity/observation regression: $required"
    }
}
if ($objective.Contains('seconds >= 30') -or
    $objective.Contains('FindPerson observation progress:') -or
    $objective.Contains('/30 seconds')) {
    throw 'FindPerson observation still contains the old 30-second threshold or obsolete progress milestones.'
}

$findPersonBranch = $objective.IndexOf('else if (step.Action is FindPersonAction)')
$selectionReset = $objective.IndexOf('Targeting.Target?.Identity != step.Target', $findPersonBranch)
$proofThreshold = $objective.IndexOf('now - step.ObservationStarted >= FindPersonObservationConfirmation', $selectionReset)
$proofSet = $objective.IndexOf('step.Proof = true;', $proofThreshold)
$reconcile = $objective.IndexOf('_completion.Reconcile(', $proofSet)
$rewardConfirmed = $objective.IndexOf('RewardConfirmed = true;', $reconcile)
$release = $objective.IndexOf('FindPerson completion release: exact target', $rewardConfirmed)
if ($findPersonBranch -lt 0 -or $selectionReset -le $findPersonBranch -or
    $proofThreshold -le $selectionReset -or $proofSet -le $proofThreshold) {
    throw 'FindPerson exact-target selection, interruption reset, or five-second proof ordering regressed.'
}
if ($reconcile -le $proofSet -or $rewardConfirmed -le $reconcile -or $release -le $rewardConfirmed) {
    throw 'FindPerson proof can bypass acknowledgement reconciliation or release before accepted completion.'
}
Write-Output 'PASS exact FindPerson target requires an uninterrupted five-second selection before proof'
Write-Output 'PASS interrupted selection resets observation and proof alone cannot bypass acknowledgement'
Write-Output 'PASS accepted FindPerson completion releases the target after reconciliation'

foreach ($required in @(
    'GuardReservedEnemy(ordinaryEnemiesRemain)',
    'reservedAggro && (_objective.HasKillTarget || _objective.HasFindPersonTarget)',
    'bool findPersonHeld = findPersonTarget != Identity.None && _objective.HoldEnemy(findPersonTarget)',
    'bool attackingFindPerson = findPersonHeld &&',
    'if (!findPersonHeld)',
    '_reservedProtectionStarted = DateTime.MinValue',
    'bool findPersonReserved = findPersonHeld &&',
    'Target remains held while ordinary enemies are cleared',
    'ordinary enemies=',
    'ReservedProtectionTimeout = TimeSpan.FromSeconds(5)',
    'player.StopAttack()',
    'Stopping to prevent an unsafe reserved-target kill')) {
    if (-not $dungeon.Contains($required)) {
        throw "Hostile FindPerson sweep/protection regression: $required"
    }
}
if ($dungeon.Contains('(attackingReserved || reservedAggro) && !_objective.HasKillTarget)')) {
    throw 'FindPerson reserved aggro still follows the old whole-dungeon stop path.'
}
if ($dungeon.Contains('bool attackingFindPerson = findPersonTarget != Identity.None &&')) {
    throw 'Released FindPerson targets can still enter the reserved-target protection path.'
}
Write-Output 'PASS early hostile FindPerson aggro continues ordinary room sweep while attacks are cancelled with a bounded safety stop'
Write-Output 'PASS completed FindPerson targets bypass protection timeout and reset stale protection state'

foreach ($required in @(
    '_objective.HoldEnemy(x.Identity)',
    'Playfield.Rooms.All',
    'NextCombatSweepRoom(room)',
    'RouteToRoom(objectiveRoom.Value)',
    '_combatCheckedRooms.Count > 0')) {
    if (-not $dungeon.Contains($required)) {
        throw "FindPerson cross-room/no-stall regression: $required"
    }
}
Write-Output 'PASS hostile FindPerson reservation does not replace ordinary-enemy sweep, cross-room routing, or deferred-loot completion'

foreach ($required in @(
    'TickKillPersonFinale(room)',
    'public Identity KillTarget',
    'if (_combat.Target == targetId) _objective.ArmKill(enemy);')) {
    if (-not ($objective + $dungeon).Contains($required)) {
        throw "KillPerson ownership regression: $required"
    }
}
Write-Output 'PASS KillPerson exact-target finale ownership remains intact'

# Execute the production lifecycle predicate: incoming exact FindPerson aggro
# remains recognized after finale activation, while released/other targets do not.
$gate = [regex]::Match($dungeon, 'internal static bool ReservedAggroActive\(bool finaleReady, bool exactHeldFindPersonAggro,\s*int hostileHeldCount\) => ([^;]+);').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($gate)) { throw 'Reserved aggro lifecycle predicate missing.' }
Add-Type -TypeDefinition ('public static class FindPersonAggroGate { public static bool Active(bool finaleReady, bool exactHeldFindPersonAggro, int hostileHeldCount) => ' + $gate + '; }')
if (-not [FindPersonAggroGate]::Active($false,$true,1) -or
    -not [FindPersonAggroGate]::Active($true,$true,1) -or
    [FindPersonAggroGate]::Active($true,$false,1) -or
    [FindPersonAggroGate]::Active($true,$true,0)) {
    throw 'Finale activation lost exact protected aggro ownership or released/unrelated aggro bypassed the gate.'
}
Write-Output 'PASS production C# predicate preserves exact protected FindPerson aggro across finale activation and rejects released/unrelated targets'
foreach ($required in @(
    'findPersonHeld && hostileReserved.Any(x => x.Identity == findPersonTarget)',
    'HoldFindPersonFinaleRoute(room, objectiveRoom.Value)',
    'FindPersonFinaleRouteWaitLimit = TimeSpan.FromSeconds(30)',
    'current room={room.Instance}, target room={targetRoom}',
    'stopped without attacking the protected target or accepting completion.')) {
    if (-not $dungeon.Contains($required)) { throw "Finale routing recovery invariant missing: $required" }
}
$routeStart = $dungeon.IndexOf('int? objectiveRoom = _objective.PendingRoom;', $dungeon.IndexOf('if (reservedAggro &&'))
$routeEnd = $dungeon.IndexOf('else ResetFindPersonFinaleRouteWait();', $routeStart)
$routeBody = $dungeon.Substring($routeStart, $routeEnd - $routeStart)
if ($routeBody.IndexOf('RouteToRoom(objectiveRoom.Value)') -gt $routeBody.IndexOf('HoldFindPersonFinaleRoute(room, objectiveRoom.Value)') -or
    -not $routeBody.Contains('ResetFindPersonFinaleRouteWait();')) {
    throw 'Finale routing must attempt mapped travel first and reset waiting on successful route ownership.'
}
$waitStart = $dungeon.IndexOf('private bool HoldFindPersonFinaleRoute(')
$waitEnd = $dungeon.IndexOf('private bool GuardReservedEnemy(', $waitStart)
$waitBody = $dungeon.Substring($waitStart, $waitEnd - $waitStart)
if (-not $waitBody.Contains('Stop();') -or $waitBody.Contains('StopAttack()') -or
    $waitBody.Contains('BeginFinale()') -or $waitBody.Contains('RewardConfirmed =')) {
    throw 'Unavailable-route recovery must stop explicitly without weakening target protection or faking completion.'
}
Write-Output 'PASS unavailable cross-room finale route retries with bounded diagnostics and explicit failure while protection stays active'
