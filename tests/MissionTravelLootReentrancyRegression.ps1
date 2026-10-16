$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$loot = Get-Content -LiteralPath (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw

function Assert($condition, $message) { if (-not $condition) { throw $message }; "PASS $message" }

$diagnostic = [regex]::Match($bot, 'private void ReportLocalRouteModeChoice\(float distance\)(?<body>[\s\S]*?)private void ResetLocalRouteModeDiagnostic').Groups['body'].Value
Assert ($diagnostic.Contains('_lastLocalRouteModeMission == _selected.Id') -and
    $diagnostic.Contains('_lastLocalRouteModeSelection') -and
    $diagnostic.Contains('_lastLocalRouteVehicleStatus')) 'local route choice logging is keyed by mission, selected mode, and vehicle status'
Assert (-not $diagnostic.Contains('_lastLocalRouteModeDistance') -and
    $diagnostic.Contains('distance={distance:0.0}m')) 'distance remains useful in emitted diagnostics but cannot trigger repeated logs'
Assert ($bot.Contains('ResetLocalRouteModeDiagnostic();')) 'route-choice diagnostic state resets across journey lifecycle boundaries'

$begin = [regex]::Match($loot, 'public void BeginMissionRoom\(int roomId\)(?<body>[\s\S]*?)public void ObserveMissionRoomTraversal').Groups['body'].Value
Assert ($begin.Contains('bool enteringMission = MissionRoomId < 0;') -and
    $begin.Contains('bool roomChanged = MissionRoomId != roomId;') -and
    $begin.Contains('if (!roomChanged) return;')) 'repeated same-room updates are idempotent and cannot restart ManagerLoot'
Assert ($begin.IndexOf('BeginMissionControl();') -lt $begin.IndexOf('if (!roomChanged) return;')) 'same-room calls preserve mission-control activation and chest enablement'
Assert ($begin.Contains('if (enteringMission && !IsProcessingMissionLoot)') -and
    $begin.Contains('CurrentProcess = ProcessState.Load_Backpacks;')) 'first mission room initializes bags without clobbering active loot on room transitions'

$load = [regex]::Match($loot, 'case ProcessState.Load_Backpacks:(?<body>[\s\S]*?)case ProcessState.Open_Corpse:').Groups['body'].Value
Assert ($load.Contains('InitializeBackpackInfo();') -and
    -not $load.Contains('CurrentProcess = ProcessState.Open_Corpse;')) 'Load_Backpacks waits for the loader positive-confirmation state instead of advancing blindly'
Assert ($loot.Contains('CurrentProcess == ProcessState.Move_To_KeepBag') -and
    $loot.Contains('CurrentProcess == ProcessState.PickingLock') -and
    $loot.Contains('CurrentProcess == ProcessState.Close_Corpse')) 'Keep placement, lockpicking, and container closure remain recognized as in-flight loot'
Assert ($loot.Contains('if (_settings["Print"].AsBool() && CurrentProcess != LastProcess)')) '/printitems only suppresses process logging and does not drive process state'
