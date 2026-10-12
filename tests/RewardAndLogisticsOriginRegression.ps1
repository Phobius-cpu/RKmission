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
Assert ($policy.Contains('Inventory.GetContainerItems(x.Identity).Count < 21')) `
    'RK Junk eligibility uses the loaded container snapshot rather than the backpack wrapper count'
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
Assert (-not $bot.Contains('string site = _logisticsNavigator.FindVerifiedOriginSite();')) `
    'automatic logistics cannot select an unrelated nearby surveyed origin'
