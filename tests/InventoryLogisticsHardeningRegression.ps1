param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root "RKmission/bin/$Configuration/net48/RKmission.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build RKmission/RKmission.csproj first.' }
$assembly = [Reflection.Assembly]::LoadFrom($dll)
$family = $assembly.GetType('ManagerLoot.ManagedBagFamily', $true)
$matches = $family.GetMethod('Matches'); $order = $family.GetMethod('Order')
function Assert($condition, $message) { if (-not $condition) { throw $message }; "PASS $message" }
function Match($bag, $request) { $matches.Invoke($null, @($bag, $request)) }
function Order($bag, $request) { $order.Invoke($null, @($bag, $request)) }
Assert (Match ' rk   loot  02 ' 'RK Loot') 'canonical matching tolerates benign whitespace/case'
Assert (Match 'RKM Keep 09' 'RK Loot') 'legacy Keep aliases map to Loot family'
Assert (Match 'RKM Sell 02' 'RK Junk') 'legacy Sell aliases map to Junk family'
Assert (-not (Match 'RK Loot 03' 'RK Loot 02')) 'explicit numbered destination stays exact'
Assert (-not (Match 'fannypack' 'RK Loot')) 'fannypack stays custom'
Assert (-not (Match 'syncbag' 'RK Junk')) 'syncbag stays custom'
Assert ((Order 'RK Loot 2' 'RK Loot') -eq [int]::MaxValue) 'one-digit lookalike is not a managed member'
Assert ((Order 'RK Loot 10' 'RK Loot') -gt (Order 'RK Loot 02' 'RK Loot')) 'managed suffix ordering is numeric'
Assert ((Order 'RK Loot 99' 'RK Loot') -lt (Order 'RKM Keep 01' 'RK Loot')) 'canonical Loot precedes legacy Keep'

$carried = Get-Content (Join-Path $root 'RKmission/CarriedItems.cs') -Raw
$dungeon = Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$loot = Get-Content (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$ready = Get-Content (Join-Path $root 'RKmission/MissionReadiness.cs') -Raw
$cycle = Get-Content (Join-Path $root 'RKmission/AutomaticLogisticsCycle.cs') -Raw
$route = Get-Content (Join-Path $root 'RKmission/LogisticsRouteNavigator.cs') -Raw
Assert ($carried.Contains('Inventory.Backpacks.SelectMany') -and $carried.Contains('Personal Lockpick')) 'carried lookup covers arbitrary backpacks and conservative lockpick names'
Assert ($dungeon.Contains('CarriedItems.FindLockpick()')) 'locked doors use shared carried lookup'
Assert ($loot.Contains('RKmission.CarriedItems.FindLockpick()')) 'locked chests use shared carried lookup'
Assert ($ready.Contains('foreach (Item item in CarriedItems.All())')) 'recovery uses shared carried enumeration'
Assert ($cycle.Contains('Phase.NormalizeInventory') -and $cycle.Contains('managed inventory normalization failed')) 'normalization gates destructive planning'
Assert ($cycle.Contains('unprocessed planned bank Keep=0') -and $cycle.Contains('Keep in RK Junk=0')) 'final reconciliation checks stranded Keep'
Assert ((Get-Content (Join-Path $root 'RKmission/ManagedInventoryNormalizer.cs') -Raw).Contains('staging bank-eligible Keep')) 'bagged bank fallback is staged to main before deposit'
$normalizer = Get-Content (Join-Path $root 'RKmission/ManagedInventoryNormalizer.cs') -Raw
Assert ($normalizer.Contains('private bool _toMain, _pending;') -and
    $normalizer.Contains('sourceNow == _sourceBefore - 1') -and
    $normalizer.Contains('destinationNow == _destinationBefore + 1')) 'normalization verifies exact source and destination count deltas without using Identity.None as pending state'
Assert ($normalizer.Contains('StagedForBank >= _maximumBankStages') -and
    $cycle.Contains('_normalizer.Start(_maximumItems)')) 'bank staging respects the requested automatic-cycle item limit'
Assert ($cycle.Contains('BaggedBankEligibleCount()')) 'final reconciliation detects bank-eligible Keep still inside bags'
Assert ($loot.Contains('PreserveVerifiedKeepOwnership(Item source, Item destination)') -and
    $loot.Contains('item.Slot == selected.SourceSlot') -and
    $normalizer.Contains('_loot.PreserveVerifiedKeepOwnership(_sourceItem, destinationItem)')) 'verified normalization rebinds Keep by usable identity or exact destination slot/details'
Assert ($normalizer.Contains('LowId == _itemLowId') -and $normalizer.Contains('HighId == _itemHighId') -and
    $normalizer.Contains('_destinationSlotsBefore')) 'normalization uses exact detail and one-item destination evidence'
Assert ($normalizer.Contains('bank-stage limit') -and $normalizer.Contains('shop execution is blocked')) 'bank cap cannot leave Keep in Junk and proceed to sale'
Assert ($cycle.Contains('TryValidateReservations(_normalizer.BankReservations') -and
    $cycle.Contains('bank transaction was held')) 'bank target cannot silently skip a missing or ambiguous planned Keep reservation'
Assert (([regex]::Matches($cycle, '_normalizer\.MisplacedKeepCount\(\)').Count -ge 3) -and
    $cycle.Contains('shop phase held:') -and $cycle.Contains('shop phase held after snapshot refresh:')) 'shop is gated on zero misplaced Keep before and after travel'
Assert ($cycle.Contains('_expectedRemainingRejects = rejectEligible - _plannedRejects') -and
    $cycle.Contains('extra Reject beyond requested batch={saleEligible} (expected)')) 'bounded sale reconciliation accepts Rejects beyond cycle N'
Assert ($cycle.Contains('_expectedRemainingBankKeeps = bankEligible - _plannedBankKeeps') -and
    $cycle.Contains('unprocessedPlannedBankKeeps = _normalizer.BankReservations.Count')) 'bounded bank reconciliation requires all planned reservations to be consumed'
Assert ($route.Contains('DynelManager.LocalPlayer.MovementState == MovementState.Fly')) 'walking routes reject Fly state at start and return'
