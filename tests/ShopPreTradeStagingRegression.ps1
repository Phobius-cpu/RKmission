param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$shop = Get-Content -LiteralPath (Join-Path $root 'RKmission/ShopSaleTest.cs') -Raw
$cycle = Get-Content -LiteralPath (Join-Path $root 'RKmission/AutomaticLogisticsCycle.cs') -Raw

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

$start = $shop.IndexOf('public bool Start(int count, bool automaticRun = false)')
$select = $shop.IndexOf('_phase = Phase.Selecting;', $start)
$move = $shop.IndexOf('staged.Item.MoveToInventory();', $select)
$open = $shop.IndexOf('exactActor.Use();', $move)
$add = $shop.IndexOf('BeginAdd(item);', $open)
Assert ($start -ge 0 -and $select -gt $start -and $move -gt $select -and $open -gt $move -and $add -gt $open) `
    'RK Junk item moves and verifies before the exact NPC trade opens or receives AddItem'

$stageBlock = $shop.Substring($shop.IndexOf('if (_phase == Phase.StageMoveSent)'),
    $shop.IndexOf('if (_phase == Phase.WaitingToOpen)') - $shop.IndexOf('if (_phase == Phase.StageMoveSent)'))
Assert (-not $stageBlock.Contains('Spell.HasPendingCast') -and
    $shop.Contains('safe window to open the shop did not arrive after verified staging')) `
    'item-location proof succeeds during a background cast while trade opening waits finitely'

Assert ($stageBlock.Contains('TimedOut(8)') -and
    $stageBlock.Contains('pre-trade move was not verified') -and
    $open -gt ($shop.IndexOf('TimedOut(8)', $shop.IndexOf('if (_phase == Phase.StageMoveSent)')))) `
    'a missing bag-to-main move fails on a bound before any sale attempt'

Assert ($shop.Contains('_policy.CanAutoSell(x.Item, _loot, true, false, out _)') -and
    $shop.Contains('sellItems.Count(y => SameItem(x.Item, y.Item)) == 1') -and
    $shop.Contains('main.All(y => !SameItem(x.Item, y))')) `
    'Keep, Protected, duplicate bag items, and matching main items remain excluded'

Assert ($shop.Contains('_completeSeen && currentMain == _mainBefore - 1 && cash > _cashBefore')) `
    'server completion alone cannot verify a sale without item removal and positive cash evidence'

foreach ($trace in @('sourceBag=', 'sourceOpenBefore=', 'sourceOpenNow=', 'snapshotItems=',
    'sourceSlot=', 'mainSlot=', 'pendingUse=', 'pendingCast=', 'movement=', 'activeTrade=',
    'shopActor=', 'elapsed=')) {
    Assert ($shop.Contains($trace)) "staging failure diagnostics include $trace"
}

Assert ($shop.Contains('ManagedBagOpenOwnership.Acquire(BagOwner, staged.Bag);') -and
    $shop.Contains('ManagedBagOpenOwnership.Release(BagOwner, _sourceBag);') -and
    $shop.Contains('ManagedBagOpenOwnership.ReleaseAll(BagOwner);')) `
    'shop staging owns the source bag lease through verification and releases it on success/failure'

$closing = $shop.IndexOf('_phase = Phase.ClosingBetweenSales;')
$decline = $shop.IndexOf('Trade.Decline();', $closing)
$closingHandler = $shop.Substring($shop.IndexOf('if (_phase == Phase.ClosingBetweenSales)'),
    $shop.IndexOf('if (_phase == Phase.ClosingFinal)') - $shop.IndexOf('if (_phase == Phase.ClosingBetweenSales)'))
Assert ($closing -ge 0 -and $decline -gt $closing -and
    $closingHandler.Contains('_phase = Phase.Selecting;') -and $closingHandler.Contains('trade closure verified')) `
    'each subsequent sale closes the prior trade and verifies closure before isolated staging'

Assert ($cycle.Contains('if (_shop.Result != VerifiedOperationResult.Succeeded)') -and
    $cycle.Contains('Fail("shop sale was not verified: "') -and
    $cycle.Contains('No failed route, transfer, or trade action was retried.')) `
    'automatic logistics stops on uncertain shop staging and does not retry the mission-cycle action'

$dll = Join-Path $root "RKmission/bin/$Configuration/net48/RKmission.dll"
Assert (Test-Path -LiteralPath $dll) 'release assembly exists for AOSharp compile regression'
