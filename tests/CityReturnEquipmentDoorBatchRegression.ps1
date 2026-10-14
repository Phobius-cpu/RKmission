$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$flight=Get-Content (Join-Path $root 'RKmission/PostZoneFlightSafety.cs') -Raw
$policy=Get-Content (Join-Path $root 'RKmission/InventoryPolicy.cs') -Raw
$dungeon=Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$bot=Get-Content (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
function Require([bool]$ok,[string]$why){if(-not $ok){throw $why}}
$city=[regex]::Match($flight,'internal static bool IsCityReturnPlayfield\(int playfield\) =>\s*([^;]+);').Groups[1].Value
$equipment=[regex]::Match($policy,'internal static bool IsProtectedCombatEquipmentTemplate\(int lowId, int highId\) =>\s*([^;]+);').Groups[1].Value
$door=[regex]::Match($dungeon,'internal static double DoorProbeSeconds\(bool locked\) => ([^;]+);').Groups[1].Value
Require ($city.Length -gt 0 -and $equipment.Length -gt 0 -and $door.Length -gt 0) 'Production batch predicates missing.'
Add-Type -TypeDefinition ('public static class ReturnBatch { public static bool City(int playfield)=>'+$city+'; public static bool Equipment(int lowId,int highId)=>'+$equipment+'; public static double Door(bool locked)=>'+$door+'; }')
foreach($pf in @(540,655,800)){Require ([ReturnBatch]::City($pf)) "City safety climb must be suppressed in $pf"}
foreach($pf in @(570,610,4107,999)){Require (-not [ReturnBatch]::City($pf)) "Other playfield $pf must retain existing safety policy"}
Require ([ReturnBatch]::Equipment(267696,267696) -and [ReturnBatch]::Equipment(0,267696)) 'Concrete tuner must be protected by template IDs even with absent unique identity.'
Require (-not [ReturnBatch]::Equipment(55683,55682)) 'Tutoring-device mission reward must remain independently classifiable.'
Require ([ReturnBatch]::Door($true) -eq 1 -and [ReturnBatch]::Door($false) -eq 3) 'Locked-door probe must be one second without changing unlocked probing.'
Write-Output 'PASS production C# city, equipment-template and locked-door timing predicates'
Require ($flight.Contains('if (IsCityReturnPlayfield(Playfield.ModelIdentity.Instance) ||')) 'City suppression must precede climb activation.'
Require ($policy.Contains('if (IsProtectedCombatEquipmentTemplate(item.LowId, item.HighId)) return true;')) 'Equipment protection must run before Reject/Junk decisions.'
Require ($bot.Contains('_loot.OperationalItemProtected = _inventory.IsOperationalItem;')) 'ManagerLoot/sales must share operational equipment protection.'
$verified=$bot.IndexOf('if (warpExitVerified && _hasRollTerminal &&')
$eligible=$bot.IndexOf('_returnCityRouteEligible = true;', $verified)
$settle=$bot.IndexOf('_inventory.TickAfterVerifiedExit(', $verified)
Require ($verified -ge 0 -and $eligible -gt $verified -and $eligible -lt $settle) 'Completed-mission verified warp must retain city-route landing evidence across settlement.'
$start=$bot.IndexOf('private bool ReturnToRollTerminal()')
$route=$bot.IndexOf('_navRoutes.TryNavigateCityReturn(', $start)
$lookup=$bot.IndexOf('Dynel terminal =', $start)
Require ($route -lt $lookup -and $route -gt $start) 'City navpath must run before live terminal verification.'
Require ($bot.Contains('Vector3 terminalPosition = terminal?.Position ?? _rollTerminalPosition;') -and
 $bot.Contains('terminal == null && Vector3.Distance(player.Position, terminalPosition) <= 10f') -and
 $bot.Contains('TimeSpan.FromSeconds(8)') -and
 $bot.Contains('terminal != null && Vector3.Distance(player.Position, terminalPosition) <= RollTerminalApproachDistance')) 'Saved-position navigation must retain bounded near-terminal streaming and exact live arrival verification.'
Require ($dungeon.Contains('TimeSpan.FromSeconds(DoorProbeSeconds(door.IsLocked))') -and
 $dungeon.Contains('if (now - crossing.LastAction < TimeSpan.FromSeconds(2))') -and
 $dungeon.Contains('if (!CarriedItems.BackpackSnapshotsReady) return;')) 'One-second initial probe must retain lockpick readiness and retry pacing.'
Write-Output 'PASS verified direct-warp city-route ownership, streaming-before-arrival distinction, equipment hook and lockpick safety integration'
