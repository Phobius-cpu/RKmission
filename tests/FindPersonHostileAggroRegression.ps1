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
    'seconds >= 30',
    'FindPerson completion release: exact target')) {
    if (-not $objective.Contains($required)) {
        throw "FindPerson identity/observation regression: $required"
    }
}
Write-Output 'PASS FindPerson finale retains exact identity, uninterrupted 30-second observation, and post-proof release'

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
