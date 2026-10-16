$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dungeon = Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$start = $dungeon.IndexOf('private bool LootInRoom(Room room)')
$end = $dungeon.IndexOf('private void BuildLootApproaches(', $start)
Assert ($start -ge 0 -and $end -gt $start) 'LootInRoom production method was not found.'
$lootInRoom = $dungeon.Substring($start, $end - $start)

# At 3.6m from the container a valid 2.5m target-side waypoint can be only 1.1m
# away. That satisfied the former coarse 1.5m arrival test, but must not satisfy
# the new precise 0.35m final settlement.
$targetDistance = 3.6
$waypointDistance = 1.1
$coarseReached = $waypointDistance -lt 1.5
$preciseReached = $waypointDistance -lt 0.35
Assert ($coarseReached -and -not $preciseReached) 'The 3.6m early-stop fixture does not reproduce the tolerance gap.'
Assert ($lootInRoom.Contains('bool preciseContainerFinal = next.Identity.Type == IdentityType.Container') -and
    $lootInRoom.Contains('if (preciseContainerFinal) arrivalTolerance = 0.35f;') -and
    $lootInRoom.Contains('(!rangeReady || !currentRayClear) && _lootContainerFinalReissues < 1') -and
    $lootInRoom.Contains('reissued precise container-side approach')) `
    'Containers outside range/ray readiness do not receive one precise final reissue.'
Write-Output 'PASS 3.6m controller early-stop keeps the validated container side active for one precise reissue'

$containerRecovery = $lootInRoom.IndexOf('if (preciseContainerFinal && !reachedApproach')
$blockedCheck = $lootInRoom.IndexOf('if (targetSideClear)', $containerRecovery)
$reissue = $lootInRoom.IndexOf('SetNavDestination(_requestedOwner, finalPoint)', $blockedCheck)
$reject = $lootInRoom.IndexOf('precise container recovery rejected blocked/invalid target-side geometry', $reissue)
Assert ($containerRecovery -ge 0 -and $blockedCheck -gt $containerRecovery -and
    $reissue -gt $blockedCheck -and $reject -gt $reissue -and
    $lootInRoom.Contains('targetSideDistance >= 2f') -and
    $lootInRoom.Contains('targetSideDistance <= LootUseRange && targetSideRayClear')) `
    'A blocked or invalid container-side point can still be reissued.'
Write-Output 'PASS blocked side geometry is diagnosed and rejected instead of driven or used'

$ready = $lootInRoom.IndexOf('bool interactionReady = LootInteractionReady(next);')
$approach = $lootInRoom.IndexOf('if (!interactionReady || _lootProbeNextSide)', $ready)
$release = $lootInRoom.IndexOf('ownership released to ManagerLoot', $approach)
Assert ($ready -ge 0 -and $approach -gt $ready -and $release -gt $approach -and
    $lootInRoom.Contains('MovementArbiter.Current.Halt(_requestedOwner);') -and
    -not $lootInRoom.Contains('next.Use()')) `
    'Successful close approach no longer releases movement to ManagerLoot without direct Use spam.'
Write-Output 'PASS successful range/ray readiness halts movement and preserves ManagerLoot use authority'

Assert ($lootInRoom.Contains('next.Identity.Type == IdentityType.Corpse && !_lootTransitStaging') -and
    $lootInRoom.Contains('_lootCorpseFinalReissues < 1') -and
    $lootInRoom.Contains('reissued precise corpse-side approach') -and
    $lootInRoom.Contains('if (reachedApproach && _lootTransitStaging)')) `
    'Existing corpse precision or multi-level transit behavior changed.'
Assert ($dungeon.Contains('TryPrepareDungeonTopology(_layout,') -and
    $dungeon.Contains('verticalGap >= CombatTopologyVerticalSeparation') -and
    $dungeon.Contains('completed recorded topology')) `
    'Existing multi-level recorded topology fallback changed.'
Write-Output 'PASS corpse precision and multi-level recorded/transit behavior remain intact'

Assert ($lootInRoom.Contains('waypoint-reached={reachedApproach}') -and
    $lootInRoom.Contains('movement-stopped={movementStopped}') -and
    $lootInRoom.Contains('range-ready={rangeReady}, ray-clear={currentRayClear}')) `
    'Final diagnostics no longer distinguish waypoint, controller, range and ray state.'
Write-Output 'PASS final diagnostics distinguish waypoint proximity, movement stop, range and ray readiness'
