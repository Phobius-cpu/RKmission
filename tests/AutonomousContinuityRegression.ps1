param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$combat = Get-Content -LiteralPath (Join-Path $root 'RKmission/CombatDriver.cs') -Raw

$logistics = $bot.IndexOf('if (_autoCycle && TickScheduledAutomaticLogistics()) return;')
$accepted = $bot.IndexOf('var accepted = RunnableAcceptedMissions().ToList();')
$capacity = $bot.IndexOf('bool rollingSuspendedForCapacity')
$selection = $bot.IndexOf('accepted.Where(CanSafelyStartAcceptedMission)')
if ($logistics -lt 0 -or $accepted -le $logistics -or $capacity -le $accepted -or $selection -le $capacity -or
    -not $bot.Contains('Inventory below rolling threshold; continuing {accepted.Count} accepted mission(s), new rolling suspended.') -or
    -not $bot.Contains('no accepted runnable Rubi-Ka missions remain')) {
    throw 'Low capacity can still block accepted mission selection or permit new rolling.'
}
Write-Output 'PASS low capacity suspends rolling but continues accepted runnable missions'

if (-not $bot.Contains('accepted.All(RequiresObjectiveFreeSlot)') -or
    -not $bot.Contains('Inventory.NumFreeSlots <= 1') -or
    -not $bot.Contains('One free main-inventory slot is needed before traveling to this item objective.')) {
    throw 'FindItem/ReturnItem free-slot preflight is no longer preserved.'
}
Write-Output 'PASS item-objective missions retain their stricter free-slot preflight'

if (-not $combat.Contains('return CombatTickResult.ApproachUnavailable;') -or
    -not $dungeon.Contains('_roomStates[room.Instance] = RoomClearanceState.CombatDeferred;') -or
    -not $dungeon.Contains('MovementArbiter.Current.Halt(MovementOwner.CombatPosition);') -or
    -not $dungeon.Contains('Room remains unfinished.') -or
    -not $dungeon.Contains('mission remains armed.') -or
    -not $dungeon.Contains('recovery.Active = false;') -or
    -not $dungeon.Contains('_roomStates[room.Instance] = RoomClearanceState.Reopened;')) {
    throw 'Bounded combat recovery no longer defers, releases movement, holds armed, or reopens for revisit.'
}
Write-Output 'PASS unreachable combat defers unfinished rooms without stopping and supports revisit'
