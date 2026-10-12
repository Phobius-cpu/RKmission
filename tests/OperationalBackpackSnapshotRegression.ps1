param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$carried = Get-Content -LiteralPath (Join-Path $root 'RKmission/CarriedItems.cs') -Raw
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$loot = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$readiness = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionReadiness.cs') -Raw

function Assert($condition, $message) { if (-not $condition) { throw $message }; "PASS $message" }

Assert ($carried.Contains('Inventory.Backpacks.SelectMany')) 'arbitrary backpack contents remain part of carried-item enumeration'
Assert ($carried.Contains('MaxBackpackLoadAttempts = 3') -and
    $carried.Contains('BackpackResponseSeconds = 2.5')) 'per-bag loading uses bounded attempts and response waits'
Assert ($carried.Contains('Inventory.ContainerOpened += ContainerOpened') -and
    $carried.Contains('Inventory.GetContainerItems(pair.Key).Any()')) 'empty and populated bags have positive snapshot evidence paths'
Assert ($carried.Contains('if (Item.HasPendingUse) return;') -and
    ([regex]::Matches($carried, 'bagItem\.Use\(\);').Count -eq 1)) 'loader sends only one bag action at a time without double-use spam'
Assert (-not $carried.Contains('ManagedBagFamily') -and -not $carried.Contains('Rules.Any')) 'operational snapshot readiness is independent of ManagerLoot rules'
Assert ($bot.Contains('CarriedItems.ResetBackpackSnapshots();') -and
    $bot.Contains('CarriedItems.UpdateBackpackSnapshots();')) 'shared snapshots reset after zoning and advance from the host update loop'
Assert ($loot.Contains('if (chest.IsLocked && !RKmission.CarriedItems.BackpackSnapshotsReady)') -and
    $loot.IndexOf('if (chest.IsLocked && !RKmission.CarriedItems.BackpackSnapshotsReady)') -lt $loot.IndexOf('FinishLockpickFailure(chest.Identity, LockpickOutcome.NoLockPick)')) 'locked chests wait for carried snapshots before ownership or NoLockPick settlement'
Assert ($dungeon.Contains('if (!CarriedItems.BackpackSnapshotsReady) return;') -and
    $dungeon.IndexOf('if (!CarriedItems.BackpackSnapshotsReady) return;') -lt $dungeon.IndexOf('FailTransition("locked door requires a Lock Pick")')) 'locked doors wait without consuming attempts before lookup readiness'
Assert ($readiness.Contains('if (!CarriedItems.BackpackSnapshotsReady) return true;') -and
    $readiness.Contains('(healthLow || nanoLow) && !CarriedItems.BackpackSnapshotsReady') -and
    $readiness.Contains('foreach (Item item in CarriedItems.All())')) 'recovery waits for and consumes the same carried snapshot visibility'
Assert (-not $loot.Contains('item?.Use(); // Open') -and -not $loot.Contains('item?.Use(); // Close')) 'ManagerLoot no longer duplicates immediate open/close bag actions'
Assert ($loot.Contains('RKmission.CarriedItems.UpdateBackpackSnapshots();') -and
    $loot.Contains('RKmission.CarriedItems.BackpackSnapshotsReady')) 'managed Loot and Junk bag loading delegates to the shared loader'
