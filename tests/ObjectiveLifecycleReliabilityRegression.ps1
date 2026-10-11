$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$objective = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionObjective.cs') -Raw
$completion = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCompletionTracker.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw

foreach ($required in @(
    'private const float CloseRangeOverride = 1.5f;',
    'private const float InteractionRayPlayerHeight = 1.1f;',
    'private const float InteractionRayTargetHeight = 0.25f;',
    'bool rayAllowsInteraction = elevatedRay || closeOverride;',
    'if (!inRange || !rayAllowsInteraction)',
    'player + Vector3.Up * InteractionRayPlayerHeight',
    'target.Position + Vector3.Up * InteractionRayTargetHeight')) {
    if (-not $objective.Contains($required)) { throw "Objective interaction policy missing: $required" }
}
Write-Output 'PASS elevated objective ray and conservative same-room close override are present'

foreach ($required in @(
    'ReadyToInteract, ActionSent, AwaitingProof',
    'private static readonly TimeSpan ApproachTimeout',
    'private static readonly TimeSpan AcknowledgementTimeout',
    'distance + 0.25f < step.BestDistance',
    'step.ApproachStarted = now;',
    'Objective action was never sent',
    'supported proof/acknowledgement')) {
    if (-not $objective.Contains($required)) { throw "Objective phase/timer regression: $required" }
}
Write-Output 'PASS approach progress, action dispatch, and proof acknowledgement use distinct states/timers'

foreach ($required in @(
    'Objective gate: reason=',
    'raw ray={rawRay}, elevated ray={elevatedRay}',
    'close override={closeOverride}',
    'ManagerLoot processing={loot.IsProcessingMissionLoot}',
    'source available=',
    'Final objective action:')) {
    if (-not $objective.Contains($required)) { throw "Objective diagnostics regression: $required" }
}
Write-Output 'PASS bounded gate and owned-action diagnostics contain blocker evidence'

if (-not $objective.Contains('if (step.Action is KillPersonAction) continue; // The guarded combat path owns this step.') -or
    -not $objective.Contains('if (CollectedReturnItem) return false;') -or
    -not $objective.Contains('disappearance alone is not accepted') -or
    -not $objective.Contains('bool ownedAction = _steps.Any(x => x.Sent);') -or
    -not $completion.Contains('bool ownedFinalAction')) {
    throw 'Kill ownership, return-item lifecycle, or proof policy regressed.'
}
Write-Output 'PASS combat ownership, return-item hand-in, and proof requirements remain guarded'

$finale = $dungeon.IndexOf('if (!BeginObjectiveFinale()) return;')
$currentRoom = $dungeon.IndexOf('if (_objective.PendingRoom == room.Instance)', $finale)
$exit = $dungeon.IndexOf('BeginExit();', $currentRoom)
if ($finale -lt 0 -or $currentRoom -le $finale -or $exit -le $currentRoom) {
    throw 'Current-room objective no longer gets a final turn before exit.'
}
Write-Output 'PASS current-room objective is processed before dungeon exit'

$auto = $bot.IndexOf('case "auto":')
$resume = $bot.IndexOf('AcceptedMission currentDungeon = ExactCurrentDungeonMission();', $auto)
$roller = $bot.IndexOf('if (!_roller.ShowRoller())', $auto)
if ($resume -lt 0 -or $roller -le $resume -or
    -not $bot.Contains('_checkpoint.Armed = false;') -or
    -not $bot.Contains('remaining safely stopped until explicit /rkm start or /rkm auto')) {
    throw 'Safe stop/reload or exact-dungeon auto resume ordering regressed.'
}
Write-Output 'PASS explicit auto resumes an exact current dungeon before roller flow and reload stays disarmed'

foreach ($required in @(
    'TimeSpan.FromSeconds(60)',
    'Dungeon association retry: attempt=',
    'movement and exploration remain held pending exact AO# identity evidence',
    'no mission identity will be guessed',
    'exact association retries are exhausted')) {
    if (-not $bot.Contains($required)) { throw "Dungeon reassociation regression: $required" }
}
Write-Output 'PASS post-zone mission reassociation is bounded, exact-only, and held safe'
