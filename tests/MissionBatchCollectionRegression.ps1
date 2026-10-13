param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$readiness = Get-Content -LiteralPath (Join-Path $root 'RKmission/CycleReadiness.cs') -Raw
$catalog = Get-Content -LiteralPath (Join-Path $root 'RKmission/LogisticsRouteCatalog.cs') -Raw
$policy = Get-Content -LiteralPath (Join-Path $root 'RKmission/InventoryPolicy.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert ($bot.Contains('if (!ReadyToRefill(out string unresolved))') -and
    $bot.Contains('return CycleReadiness.IsReadyToRefill(BuildRollReadinessSnapshot(), out unresolved);')) `
    'batch collection does not use its dedicated refill gate'
Assert ($readiness.Contains('public static bool IsReadyToRefill') -and
    -not $readiness.Substring($readiness.IndexOf('public static bool IsReadyToRefill'),
        $readiness.IndexOf('private static bool Fail') - $readiness.IndexOf('public static bool IsReadyToRefill')).Contains('NoAcceptedWork')) `
    'batch refill incorrectly rejects the first outstanding mission'
foreach ($guard in @('PriorAcceptanceSettled', 'QuestDetailsSettled', 'NoPendingItemAction',
    'NoOwnedMovement', 'ExactRollTerminalVerified', 'HasBatchCapacity', 'UnderMissionLimit')) {
    Assert ($readiness.Contains("!state.$guard")) "batch refill guard missing: $guard"
}
Assert ($bot.Contains('QuestDetailsSettled = _awaitingQuestDetails.Count == 0') -and
    $bot.Contains('UnderMissionLimit = _maxAutoMissions == 0 || _autoAcceptedCount < _maxAutoMissions')) `
    'quest-detail or finite batch bounds are not connected to refill readiness'
Write-Output 'PASS 1/2 can refill while unresolved acceptance details and 2/2 remain fail-closed'

Assert ($bot.Contains('if (!ReadyToRoll(out string finalInvariant))') -and
    $readiness.Contains('if (!state.NoAcceptedWork) return Fail("accepted mission work remains"')) `
    'final logistics/disarm no longer requires all accepted missions to finish'
Assert ($bot.IndexOf('QueueAutomaticLogistics("the finite mission batch is complete') -lt
    $bot.IndexOf('if (!ReadyToRoll(out string finalInvariant))')) `
    'finite batch no longer performs final logistics before strict disarm'
Write-Output 'PASS both missions remain outstanding work until final logistics and strict disarm'

Assert ($catalog.Contains('TryFindTerminalApproach') -and
    $catalog.Contains('route.OriginPosition') -and
    $bot.Contains('_movement.SetDestination(MovementOwner.OutdoorTravel, approach);') -and
    -not $bot.Contains('_movement.SetDestination(MovementOwner.OutdoorTravel, _rollTerminalPosition);')) `
    'terminal return still targets the solid terminal centre instead of a surveyed standoff'
Assert ($bot.Contains('_returnNoProgressAttempts >= 4') -and
    $bot.Contains('player-to-standoff=') -and $bot.Contains('player-to-terminal=')) `
    'terminal approach lacks bounded non-progress failure diagnostics'
Write-Output 'PASS exact terminal return uses a surveyed interaction standoff and bounded retries'

Assert ($policy.Contains('YalmahaXl29478ItemId = 117322') -and
    $policy.Contains('if (item.Id == YalmahaXl29478ItemId) return true;') -and
    -not $policy.Contains('IsVehicle') -and -not $policy.Contains('IndexOf("vehicle"')) `
    'known Yalmaha protection is absent or overly broad'
Write-Output 'PASS exact known Yalmaha model/id is operational without broad vehicle matching'

$dll = Join-Path $root "RKmission/bin/$Configuration/net48/RKmission.dll"
Assert (Test-Path -LiteralPath $dll) 'Build RKmission/RKmission.csproj before running this check.'
$assembly = [System.Reflection.Assembly]::LoadFrom($dll)
$snapshotType = $assembly.GetType('RKmission.CycleReadinessSnapshot', $true)
$readinessType = $assembly.GetType('RKmission.CycleReadiness', $true)
$snapshot = [System.Activator]::CreateInstance($snapshotType, $true)
foreach ($property in $snapshotType.GetProperties()) { $property.SetValue($snapshot, $true) }
$snapshotType.GetProperty('NoAcceptedWork').SetValue($snapshot, $false)
$refill = $readinessType.GetMethod('IsReadyToRefill', [System.Reflection.BindingFlags]'Public,Static')
$args = @($snapshot, $null)
Assert ($refill.Invoke($null, $args)) '1/2 refill rejected because the first mission is still outstanding.'
$snapshotType.GetProperty('UnderMissionLimit').SetValue($snapshot, $false)
$args = @($snapshot, $null)
Assert (-not $refill.Invoke($null, $args)) '2/2 refill exceeded the configured mission limit.'
Assert ($args[1] -eq 'the configured mission limit is already reached') 'wrong mission-limit diagnostic'
$snapshotType.GetProperty('UnderMissionLimit').SetValue($snapshot, $true)
$snapshotType.GetProperty('QuestDetailsSettled').SetValue($snapshot, $false)
$args = @($snapshot, $null)
Assert (-not $refill.Invoke($null, $args)) 'refill accepted unresolved quest details.'
Assert ($args[1] -eq 'accepted mission quest details are unresolved') 'wrong quest-detail diagnostic'
Write-Output 'PASS executable refill evaluator covers 1/2, 2/2, and quest-detail safety'
