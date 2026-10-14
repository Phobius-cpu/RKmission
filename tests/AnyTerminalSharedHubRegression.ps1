param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$checkpoint = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionCheckpoint.cs') -Raw
$catalog = Get-Content -LiteralPath (Join-Path $root 'RKmission/LogisticsRouteCatalog.cs') -Raw
$navigator = Get-Content -LiteralPath (Join-Path $root 'RKmission/LogisticsRouteNavigator.cs') -Raw
$cycle = Get-Content -LiteralPath (Join-Path $root 'RKmission/AutomaticLogisticsCycle.cs') -Raw
$routes = Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/LogisticsRoutes.json') -Raw | ConvertFrom-Json

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

Assert ($checkpoint.Contains('RollTerminalIdentity') -and
    $bot.Contains('_rollTerminalIdentity = terminal.Identity.ToString();') -and
    $bot.Contains('_checkpoint.RollTerminalIdentity = _rollTerminalIdentity;')) `
    'exact arbitrary rolling-terminal identity is persisted independently'
$oaAlternate = '(MissionTerminal:C007021C)'
Assert (-not ($routes.Routes.OriginTerminalIdentity -contains $oaAlternate) -and
    $bot.Contains('_rollTerminalIdentity = terminal.Identity.ToString();') -and
    $catalog.Contains('FindSharedLogisticsSite(int playfield)')) `
    'unsurveyed OA C007021C can remain the exact roller while PF540 selects the shared OA hub'
Assert ($bot.Contains('FindSharedLogisticsSite(playfield)') -and
    -not $bot.Contains('private string _rollLogisticsSite, _rollLogisticsTerminalIdentity')) `
    'city hub selection is independent of the rolling terminal identity'
Assert ($catalog.Contains('group.Any(route => route.Purpose == "bank")') -and
    $catalog.Contains('group.Any(route => route.Purpose == "shop")')) `
    'shared hub requires validated bank and shop route coverage'

$expected = @{ 540 = 'oa'; 800 = 'bor'; 655 = 'icc' }
foreach ($playfield in $expected.Keys) {
    $city = @($routes.Routes | Where-Object OriginPlayfield -eq $playfield)
    Assert (($city.Site | Select-Object -Unique) -eq $expected[$playfield] -and
        ($city.Purpose -contains 'bank') -and ($city.Purpose -contains 'shop')) `
        "PF $playfield has one complete surveyed city hub"
}
$borBank = $routes.Routes | Where-Object { $_.Site -eq 'bor' -and $_.Purpose -eq 'bank' }
$borShop = $routes.Routes | Where-Object { $_.Site -eq 'bor' -and $_.Purpose -eq 'shop' }
Assert ($borBank.OriginTerminalIdentity -ne $borShop.OriginTerminalIdentity) `
    'Borealis fixture exercises distinct bank and shop origin terminals'
Assert ($cycle.Contains('TryNavigateToOrigin(_site, "shop"') -and
    $navigator.Contains('surveyed.OriginTerminalIdentity')) `
    'Borealis bank-to-shop connector preserves per-route terminal provenance'

Assert ($navigator.Contains('TimeSpan.FromMinutes(3)') -and
    $navigator.Contains('_connectorNoProgress >= 4') -and
    $navigator.Contains('OriginPosition is a recorded reachable player location') -and
    -not $navigator.Contains('SetDestination(MovementOwner.LogisticsTravel, terminal.Position)')) `
    'hub connector is bounded and never targets a terminal centre'
Assert ($catalog.Contains('const float standoff = 2.25f') -and
    $bot.Contains('no collision-safe interaction standoff could be derived')) `
    'unsurveyed roller return derives a collision-safe live-terminal standoff'
Assert ($bot.Contains('legacyMatches.Length == 1') -and
    $bot.Contains('Migrated legacy saved roller position to unambiguous live terminal')) `
    'legacy checkpoint migration requires one unambiguous live terminal'
Assert ($bot.Contains('TerminalInstance = _rollingTerminalIdentity.Instance') -and
    $bot.Contains('origin.TerminalIdentity')) `
    'return-item quests retain their actual issuing terminal identity'
Assert ($bot.Contains('return CycleReadiness.IsReadyToRefill(BuildRollReadinessSnapshot(), out unresolved);') -and
    $bot.Contains('if (!ReadyToRoll(out string finalInvariant))')) `
    'existing-quest refill and strict post-logistics disarm gates remain separate'
