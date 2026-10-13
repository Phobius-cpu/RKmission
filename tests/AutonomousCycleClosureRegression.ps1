param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$checkpoint = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCheckpoint.cs') -Raw
$readiness = Get-Content -LiteralPath (Join-Path $root 'RKmission/CycleReadiness.cs') -Raw
$bags = Get-Content -LiteralPath (Join-Path $root 'RKmission/ManagedBagOpenOwnership.cs') -Raw
$loot = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$scotty = Get-Content -LiteralPath (Join-Path $root 'RKmission/ScottyboiWarpProvider.cs') -Raw
$fgrid = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert ($bot.Contains('if (_checkpoint.Armed)') -and
    $bot.Contains('_checkpoint.Armed = false;') -and
    $bot.Contains('remaining safely stopped until explicit /rkm start or /rkm auto')) `
    'Persisted Armed state can resume without an explicit command.'
Write-Output 'PASS reload recovers context but forcibly disarms automation'

foreach ($field in @('HandInPending', 'RewardSettlementPending',
    'InventorySettlementPending', 'LogisticsPending', 'AcceptedMissionIds',
    'LastVerifiedPhase', 'StopInvariant')) {
    Assert ($checkpoint.Contains("$field { get; set; }")) "Cycle ledger field missing: $field"
}
Write-Output 'PASS durable cycle ledger records pending work and last verified boundary'

foreach ($invariant in @('player is not outdoors', 'player is not alive', 'zoning is active',
    'accepted mission list is unavailable', 'accepted mission work remains',
    'mission hand-in remains pending', 'mission reward settlement remains pending',
    'post-mission inventory settlement remains pending',
    'automatic logistics remains pending or active', 'managed inventory snapshots are not ready',
    'an item use or spell cast remains pending', 'RKMission still owns movement',
    'saved roller terminal identity/position is not live-verified')) {
    Assert ($readiness.Contains($invariant)) "READY_TO_ROLL invariant missing: $invariant"
}
$gate = $bot.IndexOf('if (!ReadyToRoll(out string unresolved))')
$roll = $bot.IndexOf('StartZoneRolling(_autoZone)', $gate)
Assert ($gate -ge 0 -and $roll -gt $gate) 'Automatic rolling can start before READY_TO_ROLL.'
Assert ($bot.Contains('!string.IsNullOrEmpty(_rollLogisticsTerminalIdentity)') -and
    $bot.Contains('Vector3.Distance(x.Position, _rollTerminalPosition) <= 1f')) `
    'Saved roller terminal lacks exact live identity/position verification.'
Write-Output 'PASS READY_TO_ROLL is authoritative and requires exact surveyed terminal evidence'

Assert ($bot.Contains('_checkpoint.HandInPending = true;') -and
    $bot.Contains('_checkpoint.RewardSettlementPending = true;') -and
    $bot.Contains('_checkpoint.InventorySettlementPending = true;') -and
    $bot.Contains('_checkpoint.LastVerifiedPhase = "RewardAndInventorySettled"')) `
    'Hand-in/reward/inventory pending phases do not close through the shared ledger.'
Assert ($bot.Contains('Return-item hand-in confirmed for mission') -and
    $bot.Contains('_loot.ReleaseMissionReservations(_selected.Id)')) `
    'Verified return-item hand-in no longer controls reservation release.'
Write-Output 'PASS hand-in, reward, and inventory settlement block lifecycle advancement'

Assert ($loot.Contains('public void BeginMissionControl()') -and
    $loot.Contains('public void ReleaseMissionControl()') -and
    $loot.Contains('_missionControlOwned') -and
    $dungeon.Contains('_loot.BeginMissionControl();') -and
    $dungeon.Contains('_loot.EndMissionRoom(restoreUserState: false);') -and
    $dungeon.Contains('_loot.ReleaseMissionControl();')) `
    'ManagerLoot original state is not owned for the whole mission lifetime.'
Write-Output 'PASS ManagerLoot enable ownership spans the mission and restores once'

Assert ($bags.Contains('if (!bag.IsOpen) lease.OpenedByRkMission = true;') -and
    $bags.Contains('if (lease.Owners.Count > 0) continue;') -and
    $bags.Contains('Item.HasPendingUse || Spell.HasPendingCast') -and
    $bags.Contains('DateTime.UtcNow - lease.LastUse < TimeSpan.FromMilliseconds(750)')) `
    'Managed bag closure can close user-owned bags or race a pending move/use.'
Assert ($bot.Contains('ManagedBagOpenOwnership.Tick();')) `
    'Managed bag leases are never reconciled by the lifecycle loop.'
Write-Output 'PASS RKMission-opened managed bags close only after owners and pending actions release'

Assert ($scotty.Contains('if (_joinedByProvider && Team.IsInTeam) Team.Leave();') -and
    $scotty.Contains('_joinedByProvider = false;') -and
    $fgrid.Contains('if (_joinedByProvider && Team.IsInTeam)') -and
    $fgrid.Contains('Team.Leave();')) `
    'Temporary travel teams are not released strictly through provider ownership.'
Write-Output 'PASS Scotty/FGrid temporary teams clean up only when RKMission owns the join'

Assert ($bot.Contains('Finite automatic cycle finished after') -and
    $bot.Contains('_checkpoint.LastVerifiedPhase = "READY_TO_ROLL"') -and
    $bot.Contains('Lifecycle: terminal return verified -> logistics verified -> READY_TO_ROLL')) `
    'Finite cycle does not finish through verified READY_TO_ROLL and clean disarm.'
Write-Output 'PASS finite completion and unlimited reroll share READY_TO_ROLL quality gate'

$dll = Join-Path $root "RKmission/bin/$Configuration/net48/RKmission.dll"
Assert (Test-Path -LiteralPath $dll) 'Build RKmission/RKmission.csproj before running this check.'
$assembly = [System.Reflection.Assembly]::LoadFrom($dll)
$snapshotType = $assembly.GetType('RKmission.CycleReadinessSnapshot', $true)
$readinessType = $assembly.GetType('RKmission.CycleReadiness', $true)
$snapshot = [System.Activator]::CreateInstance($snapshotType, $true)
foreach ($property in $snapshotType.GetProperties()) { $property.SetValue($snapshot, $true) }
$args = @($snapshot, $null)
$method = $readinessType.GetMethod('IsReadyToRoll',
    [System.Reflection.BindingFlags]'Public,Static')
Assert ($method.Invoke($null, $args)) 'A fully satisfied readiness snapshot was rejected.'
$snapshotType.GetProperty('NoRewardPending').SetValue($snapshot, $false)
$args = @($snapshot, $null)
Assert (-not $method.Invoke($null, $args)) 'Unsettled reward did not block readiness.'
Assert ($args[1] -eq 'mission reward settlement remains pending') 'Wrong unresolved invariant diagnostic.'
Write-Output 'PASS readiness evaluator fails closed with the exact unresolved invariant'
