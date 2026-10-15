$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$d = Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$m = Get-Content (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
function Assert([bool]$ok, [string]$why) { if (-not $ok) { throw $why } }

foreach ($token in @(
    'arrivalTolerance = next.Identity.Type == IdentityType.Corpse',
    '? 0.35f : 1.5f',
    '_lootCorpseFinalReissues < 1',
    'reissued precise corpse-side approach',
    'TryRetryGuardedDeferredCorpse(guarded.Corpse, guarded.RoomId,',
    'GuardedCorpseRetryCooldown = TimeSpan.FromSeconds(5)',
    'MaxGuardedCorpseSameRoomRetries = 2',
    'PostCombatCorpseSettlementWait = TimeSpan.FromSeconds(90)',
    'guard.Corpse, guard.RoomId, guard.CorpseOccurrence)',
    'Post-combat corpse guard safe-stop',
    '_loot.RetryVisibleOutstandingMissionLoot(skipped.Corpse, skipped.RoomId, reason)'
)) { Assert ($d.Contains($token)) "Missing guarded corpse invariant: $token" }
Write-Output 'PASS exact corpse final approach and bounded in-room retry guards'

$start = $m.IndexOf('public bool TryRetryGuardedDeferredCorpse(')
$end = $m.IndexOf('public void RetryVisibleOutstandingMissionLoot(', $start)
Assert ($start -ge 0 -and $end -gt $start) 'Missing bounded guard-specific ledger release API'
$body = $m.Substring($start, $end - $start)
foreach ($token in @(
    'identity.Type != IdentityType.Corpse', 'trackedRoom != roomId',
    'trackedOccurrence != occurrence', 'state != MissionSourceState.DeferredApproach',
    '_deferredApproaches.Remove(identity)', 'SetMissionSource(identity, MissionSourceState.Discovered)'
)) { Assert ($body.Contains($token)) "Guarded reapproach is not exact occurrence scoped: $token" }
foreach ($forbidden in @('_finishedMissionLoot.Add(', '_openedMissionLoot.Add(', 'MissionLootProgress++')) {
    Assert (-not $body.Contains($forbidden)) "Guarded retry cannot create settlement evidence: $forbidden"
}
Write-Output 'PASS same-room release touches only the exact deferred corpse without fake processing credit'

$guardStart = $d.IndexOf('private bool ReconcilePostCombatCorpseGuards(')
$guardEnd = $d.IndexOf('private void ResetPostCombatCorpseTracking()', $guardStart)
$guardBody = $d.Substring($guardStart, $guardEnd - $guardStart)
Assert ($guardBody.Contains('skipped.InRoomRetryCount < MaxGuardedCorpseSameRoomRetries')) 'Skipped visible corpse retry is unbounded'
Assert ($guardBody.Contains('_loot.IsMissionLootSuccessfullyProcessed(')) 'Exact open/finish settlement proof missing'
Assert ($guardBody.Contains('TrySkipUnavailableMissionLoot(')) 'Invisible source final reconciliation missing'
Assert ($guardBody.Contains('if (!visibleInOwnedRoom')) 'Visible corpse must not be unavailable-skipped'
Write-Output 'PASS finite visible-source retries, exact settlement proof, and original 90s safety stop'
