param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$loot = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert ($loot.Contains('public bool HasReadyDeferredMissionLoot =>') -and
    $loot.Contains('_missionRoomTraversals >= x.Value.RetryAfterTraversal') -and
    $loot.Contains('state == MissionSourceState.DeferredApproach')) `
    'ManagerLoot does not expose exact ready deferred-work evidence.'

$route = $dungeon.IndexOf('Room deferredLootHop = RouteTo(room.Instance,')
$hold = $dungeon.IndexOf('if (_loot.HasReadyDeferredMissionLoot && WaitingForRoute)', $route)
$unfinished = $dungeon.IndexOf('if (_loot.UnfinishedMissionLootCount > 0)', $hold)
Assert ($route -ge 0 -and $hold -gt $route -and $unfinished -gt $hold) `
    'Temporary deferred-loot route suppression can still fall into the terminal unfinished-loot stop.'
Assert ($dungeon.Contains('without releasing ManagerLoot control')) `
    'Cooldown hold does not retain mission ownership diagnostics.'
Write-Output 'PASS ready deferred loot waits through reverse-edge cooldown without stopping mission control'

Assert ($dungeon.Contains('_deferredLootRouteWaitStarted = DateTime.MinValue;') -and
    $dungeon.Contains('Traversal requirement satisfied; routing through room') -and
    $dungeon.Contains('BeginTransition(room.Instance, deferredLootHop.Instance);')) `
    'Deferred-loot return does not reset its wait and resume the existing bounded retry route.'
Write-Output 'PASS cooldown expiry routes back through the existing deferred-approach retry path'

Assert ($dungeon.Contains('DeferredLootRouteWaitLimit = TimeSpan.FromSeconds(95)') -and
    $dungeon.Contains('DateTime.UtcNow - _deferredLootRouteWaitStarted <= DeferredLootRouteWaitLimit') -and
    $dungeon.Contains('Deferred ordinary-loot return route remained unavailable') -and
    $dungeon.Contains('mission stopped without completing or exiting')) `
    'Deferred-loot route wait lacks a bounded explicit failure.'
Write-Output 'PASS temporary route hold remains bounded and fails explicitly without false completion'

$stopStart = $dungeon.IndexOf('public void Stop()')
$stopEnd = $dungeon.IndexOf('public void Dispose()', $stopStart)
$stop = $dungeon.Substring($stopStart, $stopEnd - $stopStart)
Assert ($stop.Contains('_deferredLootRouteWaitStarted = DateTime.MinValue;')) `
    'Mission stop does not clear deferred-route wait state.'
Write-Output 'PASS deferred-route wait state resets at mission lifecycle boundaries'
