param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$policy = Get-Content -LiteralPath (Join-Path $root 'RKmission/InventoryPolicy.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$catalog = Get-Content -LiteralPath (Join-Path $root 'RKmission/LogisticsRouteCatalog.cs') -Raw
$navigator = Get-Content -LiteralPath (Join-Path $root 'RKmission/LogisticsRouteNavigator.cs') -Raw
$checkpoint = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCheckpoint.cs') -Raw

function Assert($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

Assert ($policy.Contains('Slot == item.Slot') -and
    $policy.Contains('LowId == item.LowId') -and
    $policy.Contains('IsNewMissionOccurrence(item)')) `
    'repeatable rewards use slot/detail occurrence evidence instead of UniqueIdentity-only newness'
Assert ($policy.Contains('int added = current.Count - baseline.Count;') -and
    $policy.Contains('unmatched.Count == added') -and
    $policy.Contains('without risking a pre-mission Reject')) `
    'newness multiplicity fails closed when a pre-mission equal item changed slots'
Assert ($policy.Contains('destination-count increase') -and
    $policy.Contains('bagMatchCount > _pendingBagMatchCount') -and
    $policy.Contains('!sourceStillMatches')) `
    'duplicate-identity reward moves require source-slot removal and destination-count increase'
Assert ($policy.Contains('private bool _movePending;') -and
    $policy.Contains('if (_movePending)') -and
    $policy.Contains('_movePending = true;') -and
    $policy.Contains('_movePending = false;')) `
    'Identity.None rewards retain explicit pending-transfer state through confirmation'
Assert (-not $policy.Contains('if (_pendingItem != Identity.None)')) `
    'item identity is never reused as the pending-transfer sentinel'
Assert ($policy.Contains('Inventory.GetContainerItems(x.Identity).Count < 21')) `
    'RK Junk eligibility uses the loaded container snapshot rather than the backpack wrapper count'
Assert ($policy.Contains('Inventory.ContainerOpened += OnContainerOpened;') -and
    $policy.Contains('container.Identity != _snapshotBag') -and
    $policy.Contains('_snapshotConfirmed = true;')) `
    'empty post-zone destination bags require exact ContainerOpened evidence'
Assert ($policy.Contains('observed > 0') -and
    $policy.Contains('MaximumDestinationSnapshotUses') -and
    $policy.Contains('no item move was sent')) `
    'populated snapshots are positive evidence and empty-bag refresh remains bounded and fail-closed'
Assert ($policy.IndexOf('EnsureDestinationSnapshot(bag, say, out bool snapshotBlocked)') -lt
    $policy.IndexOf('item.MoveToContainer(bag);')) `
    'post-mission staging cannot send a move before destination snapshot verification'
Assert ($policy.Contains('if (!inSellBag && !newlyAcquiredMain)')) `
    'pre-existing unstaged Rejects remain ineligible for automatic sale'

Assert ($checkpoint.Contains('RollLogisticsSite') -and
    $checkpoint.Contains('RollLogisticsTerminalIdentity')) `
    'saved roller checkpoint persists surveyed logistics provenance'
Assert ($catalog.Contains('FindUniqueOriginSite') -and
    $catalog.Contains('OriginTerminalIdentity') -and
    $catalog.Contains('matches.Length == 1')) `
    'origin binding requires one surveyed site with exact terminal identity'
Assert ($navigator.Contains('FindVerifiedOriginSite(string requiredSite = null)') -and
    $navigator.Contains('string.Equals(route.Site, requiredSite')) `
    'live origin verification is constrained to the persisted surveyed site'
Assert ($bot.Contains('TryBindSavedTerminalToSurveyedOrigin();') -and
    $bot.Contains('TimeSpan.FromSeconds(30)') -and
    $bot.Contains('waiting for its exact live terminal identity')) `
    'post-warp logistics waits boundedly for live surveyed-origin evidence'
Assert ($bot.Contains('private const float RollTerminalApproachDistance = 2f;') -and
    $bot.Contains('<= RollTerminalApproachDistance') -and
    -not $bot.Contains('_rollTerminalPosition) <= 6f')) `
    'post-warp return closes to 2m of the saved mission terminal before logistics verification'
Assert ($bot.Contains('LogisticsOriginDiagnostic(_rollLogisticsSite)') -and
    $bot.Contains('player-to-saved-terminal=') -and
    $bot.Contains('player-to-surveyed-origin=') -and
    $bot.Contains('exact terminal=')) `
    'bounded origin verification reports saved, surveyed, and exact-live-terminal evidence'
Assert (-not $bot.Contains('string site = _logisticsNavigator.FindVerifiedOriginSite();')) `
    'automatic logistics cannot select an unrelated nearby surveyed origin'
Assert ($bot.Contains('if (!_handInExitVerified && !_verifiedExitReported)') -and
    $bot.Contains('_verifiedExitReported = true;')) `
    'verified mission-exit proof is logged once while settlement spans multiple ticks'
