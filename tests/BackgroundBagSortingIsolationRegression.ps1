param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$loot = Get-Content (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$dungeon = Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
function Assert($condition, $message) { if (-not $condition) { throw $message }; "PASS $message" }

$processing = [regex]::Match($loot,
    'public bool IsProcessingMissionLoot =>(?<body>.*?);\s*public MissionIdentity ProcessingMissionLootIdentity',
    [Text.RegularExpressions.RegexOptions]::Singleline).Groups['body'].Value
Assert (-not $processing.Contains('ProcessState.Move_To_BackPack')) 'background bag sorting is not mission-source processing'
Assert ($processing.Contains('_pendingMissionLoot != MissionIdentity.None') -and
    $processing.Contains('CorpseContainer != null') -and
    $processing.Contains('ProcessState.Move_To_KeepBag')) 'pending source, opened source, and Keep placement still guard mission progress'
Assert ($dungeon.Contains('if (_loot.IsProcessingMissionLoot)') -and
    $dungeon.Contains('if (next == null)') -and
    $dungeon.Contains('return false;')) 'empty room can leave loot coordination and continue routing when only background sorting runs'

Assert ($loot.Contains('MaxBackgroundBagMoveAttempts = 3') -and
    $loot.Contains('BackgroundBagMoveConfirmationSeconds = 1.5') -and
    $loot.Contains('_backgroundBagMove.Attempts >= MaxBackgroundBagMoveAttempts')) 'failed background transfer retries are time-spaced and bounded'
Assert ($loot.Contains('sourceRemoved && destinationCount > _backgroundBagMove.DestinationCountBefore') -and
    $loot.Contains('Background bag move verified')) 'successful background transfer requires source removal and destination arrival'
Assert ($loot.Contains('Background bag move deferred') -and
    $loot.Contains('_deferredBackgroundBagSlots.Add') -and
    $loot.Contains('_deferredBackgroundBagSlots.Clear()')) 'ambiguous background transfer is diagnosed, deferred, and safely reacquired after refresh'
Assert ($loot.Contains('MissionRoomId >= 0 && _sourceAcquisition != null') -and
    $loot.Contains('CurrentProcess = ProcessState.Move_To_Inventory;')) 'mission acquisition returns to its verified source-transfer path before background sorting'
Assert ($loot.Contains('FinishMissionContainer()') -and
    $loot.Contains('_openedMissionLootOccurrences') -and
    $loot.Contains('_finishedMissionLootOccurrences')) 'background isolation does not bypass exact source occurrence settlement'
Assert ($loot.Contains('!ProtectedMissionItem(i)') -and
    $loot.Contains('GetRuleForItem(i)') -and
    $loot.Contains('invItemNoBag')) 'rule filtering, mission-item protection, and quantity selection remain in the sorter'
