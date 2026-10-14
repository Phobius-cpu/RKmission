param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$manager = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$tick = $dungeon.IndexOf('public void Tick()')
$observe = $dungeon.IndexOf('ObservePostCombatCorpseGuard();', $tick)
$reconcile = $dungeon.IndexOf('if (ReconcilePostCombatCorpseGuards(DynelManager.LocalPlayer.Room)) return;', $observe)
$pause = $dungeon.IndexOf('_loot.MissionActionsPaused = _readiness.InCombat;', $reconcile)
$readiness = $dungeon.IndexOf('if (_transition == null && HoldForReadiness()) return;', $pause)
$combat = $dungeon.IndexOf('if (_readiness.InCombat)', $readiness)
Assert ($tick -ge 0 -and $observe -gt $tick -and $reconcile -gt $observe -and
    $pause -gt $reconcile -and $readiness -gt $pause -and $combat -gt $readiness) `
    'Corpse settlement no longer advances before combat/readiness early returns.'
Write-Output 'PASS combat-blocked corpse reconciliation and its 90-second deadline run every dungeon tick'

$reconcileMethod = $dungeon.IndexOf('private bool ReconcilePostCombatCorpseGuards(Room room)')
$nextMethod = $dungeon.IndexOf('private void ResetPostCombatCorpseTracking()', $reconcileMethod)
$reconcileBody = $dungeon.Substring($reconcileMethod, $nextMethod - $reconcileMethod)
Assert (-not $reconcileBody.Contains('MovementArbiter.Current.Halt') -and
    -not $reconcileBody.Contains('RouteToRoom(') -and
    -not $reconcileBody.Contains('FightInRoom(') -and
    $reconcileBody.Contains('PostCombatCorpseSettlementWait') -and
    $reconcileBody.Contains('TrySkipUnavailableMissionLoot')) `
    'The combat-safe reconciliation pass can steal movement/combat ownership or lost its deadline.'
Write-Output 'PASS early reconciliation is evidence-only and does not halt, route, loot, or fight'

foreach ($required in @(
    'SkippedUnavailable',
    'public bool TrySkipUnavailableMissionLoot(MissionIdentity identity, int roomId, long occurrence,',
    'observedRoom != roomId',
    'currentOccurrence != occurrence',
    'IsMissionCriticalLoot(identity)',
    '_pendingMissionLoot == identity',
    '_sourceAcquisition?.Source == identity',
    'settled as unavailable without successful-loot credit')) {
    Assert ($manager.Contains($required)) "Unavailable corpse policy regressed: $required"
}
Write-Output 'PASS only an exact inactive non-critical occurrence can be classified unavailable'

Assert ($reconcileBody.Contains('visibleInOwnedRoom') -and
    $reconcileBody.Contains('exact ContainerOpened and processing-finished evidence absent') -and
    $reconcileBody.Contains('explicitly skipped unavailable ordinary corpse') -and
    $reconcileBody.Contains('Post-combat corpse guard safe-stop:') -and
    $reconcileBody.Contains('unavailable-skip refusal=')) `
    'Visible/critical safe-stop and invisible ordinary skip outcomes are no longer explicit.'
Write-Output 'PASS invisible ordinary corpses skip explicitly while visible or critical failures safe-stop'

Assert ($manager.Contains('_openedMissionLootOccurrences.TryGetValue(identity, out long openedOccurrence)') -and
    $manager.Contains('_finishedMissionLootOccurrences.TryGetValue(identity, out long finishedOccurrence)') -and
    -not $manager.Substring($manager.IndexOf('public bool TrySkipUnavailableMissionLoot'),
        $manager.IndexOf('public void RetryVisibleOutstandingMissionLoot') -
        $manager.IndexOf('public bool TrySkipUnavailableMissionLoot')).Contains('_finishedMissionLoot.Add')) `
    'Unavailable classification can claim successful processing or weaken occurrence-local evidence.'
Write-Output 'PASS unavailable settlement never creates successful-loot evidence'

Assert ($dungeon.Contains('_objective.ObserveAcknowledgement();') -and
    $dungeon.Contains('TickObjective(room)') -and
    $dungeon.Contains('reservedAggro && (_objective.HasKillTarget || _objective.HasFindPersonTarget)')) `
    'FindPerson observation/acknowledgement or reserved-target combat flow regressed.'
Write-Output 'PASS reserved FindPerson observation and acknowledgement paths remain present'
