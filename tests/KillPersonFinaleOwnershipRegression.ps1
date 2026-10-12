$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$objective = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionObjective.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

foreach ($required in @(
    'public Identity KillTarget',
    'public int? RoomFor(Identity target)',
    'TickKillPersonFinale(room)',
    'DynelManager.GetDynel(targetId)',
    '_objective.Refresh(_layout);',
    'RouteToRoom(mappedRoom.Value)',
    'ApproachCombatEnemy(target, room.Instance)',
    'if (_combat.Target == targetId) _objective.ArmKill(enemy);',
    'KillFinaleOwnershipWatchdog = TimeSpan.FromSeconds(7)',
    'KillPerson finale had no combat or movement owner',
    'bounded reacquire wait',
    'same-room exact-target transit/approach')) {
    if (-not ($objective + $dungeon).Contains($required)) {
        throw "KillPerson finale ownership regression: $required"
    }
}
Write-Output 'PASS exact KillPerson identity owns in-range, distant, cross-room, stale-room, and unloaded finale paths'

$finaleGate = $dungeon.IndexOf('_objective.Finale && _objective.FinalActionsAllowed')
$ordinaryFight = $dungeon.IndexOf('if (_readiness.InCombat)', $finaleGate)
if ($finaleGate -lt 0 -or $ordinaryFight -le $finaleGate -or
    -not $objective.Contains('!(Finale && FinalActionsAllowed)')) {
    throw 'Reserved target ordering no longer requires finale and final-action permission.'
}
Write-Output 'PASS KillPerson remains reserved until ordinary-enemy clearance allows finale actions'

foreach ($required in @(
    '_transition != null',
    '_combatRecoveries.Values.Any(x => x.Active)',
    'MovementOwner.DoorTransition',
    'BeginCombatRecovery(room, enemy)')) {
    if (-not $dungeon.Contains($required)) {
        throw "Legitimate routing/recovery ownership guard regressed: $required"
    }
}
Write-Output 'PASS doorway transitions and shared combat topology recovery suppress false unowned recovery'
