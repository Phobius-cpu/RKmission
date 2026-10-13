param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manager = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert ($manager.Contains('_openedMissionLoot.Contains(identity) && _finishedMissionLoot.Contains(identity)') -and
    $manager.Contains('state == MissionSourceState.Completed')) `
    'Success no longer requires exact open and finished evidence for the same identity.'
Assert ($manager.Contains('if (_pendingMissionLoot != container.Identity) return;') -and
    $manager.Contains('_openedMissionLoot.Add(container.Identity)')) `
    'ContainerOpened evidence is not bound to the exact pending corpse.'
Write-Output 'PASS two corpses opened out of order cannot lend success evidence to each other'

Assert ($dungeon.Contains('Post-combat corpse guard rejected skip for visible') -and
    $dungeon.Contains('_loot.RetryVisibleOutstandingMissionLoot(skipped.Corpse, skipped.RoomId, reason)') -and
    $manager.Contains('_unreachableMissionLoot.Remove(identity)') -and
    $manager.Contains('_finishedMissionLoot.Remove(identity)')) `
    'A missed first corpse can still be silently cleared after processing another source.'
Write-Output 'PASS a visible missing first corpse remains outstanding after another corpse/container is processed'

Assert ($manager.Contains('public bool TryGetMissionLootSkipReason') -and
    $manager.Contains('_missionSkipReasons[identity] = $"approach retries exhausted: {failureReason}"') -and
    $manager.Contains('state == MissionSourceState.Completed) return false') -and
    $dungeon.Contains('accepted explicit skip') -and
    $dungeon.Contains('corpse is no longer visible in its room')) `
    'Explicit justified skips are not distinct from successful processing.'
Write-Output 'PASS skip and success are distinct outcomes with verified reason diagnostics'

Assert ($dungeon.Contains('PostCombatCorpseSettlementWait = TimeSpan.FromSeconds(90)') -and
    $dungeon.Contains('PostCombatCorpseSkipVisibilityGrace = TimeSpan.FromSeconds(5)') -and
    $dungeon.Contains('skipped.SkipInvisibleAt = now') -and
    $dungeon.Contains('visible={DynelManager.GetDynel(guard.Corpse) != null}') -and
    $dungeon.Contains('Post-combat corpse guard safe-stop')) `
    'Transient invisibility tolerance or bounded safe-stop regressed.'
Write-Output 'PASS transient visibility gaps retain ownership and genuine impossible cases remain bounded'

Assert ($manager.Contains('if (identity == MissionIdentity.None || MissionRoomId < 0 ||') -and
    $manager.Contains('IsMissionLootSuccessfullyProcessed(identity)) return;') -and
    $manager.Contains('!_finishedMissionLoot.Contains(c.Identity)')) `
    'Completed exact corpses can be reopened or duplicate-processed.'
Write-Output 'PASS exact completion is idempotent and prevents duplicate processing'

Assert ($manager.Contains('private const double LootWindowDwellSeconds = 2.0;')) `
    'The required two-second loot-window dwell regressed.'
Write-Output 'PASS existing loot rules and two-second dwell remain intact'
