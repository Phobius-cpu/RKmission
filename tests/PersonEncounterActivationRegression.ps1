$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$objective=Get-Content (Join-Path $root 'RKmission/MissionObjective.cs') -Raw
$dungeon=Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
function Require([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$start=$objective.IndexOf('        public bool TryActivatePersonEncounter(')
$end=$objective.IndexOf('        public void BeginFinale()', $start)
$method=$objective.Substring($start,$end-$start)
Add-Type -TypeDefinition (@'
using System;
using System.Collections.Generic;
using System.Linq;
public class Room { public int Instance; }
public class Dynel { public int Identity; }
public class DungeonLayout { public bool ContainsDynel(int room,Dynel live)=>live.Identity==room; }
public static class DynelManager { public static Dynel Live; public static Dynel GetDynel(int identity)=>Live; }
public class ActionBase { public string Type; }
public class FindPersonAction : ActionBase {}
public class KillPersonAction : ActionBase {}
public class FindItemAction : ActionBase {}
public class Step { public bool Done; public int Target; public ActionBase Action; }
public class Encounter {
 public bool PersonEncounterActive,RewardConfirmed,FinalActionsAllowed,Finale,Preflight=true;
 public string Failure;
 public List<Step> _steps=new List<Step>(); public Dictionary<int,int> _rooms=new Dictionary<int,int>();
 public System.Action<string> _say=s=>{};
 public bool PreflightFinale(out string reason){reason="preflight blocked";return Preflight;}
 public void BeginFinale(){Finale=true;}
'@+$method+'}')
foreach($kind in @('Find','Kill')) {
 $e=[Encounter]::new(); $step=[Step]::new();$step.Target=7
 $step.Action=if($kind -eq 'Find'){[FindPersonAction]::new()}else{[KillPersonAction]::new()}
 $e._steps.Add($step);$e._rooms.Add(7,9)
 $room=[Room]::new();$room.Instance=26;$layout=[DungeonLayout]::new()
 Require (-not $e.TryActivatePersonEncounter($room,$layout)) 'Person objective must not activate in another room.'
 $room.Instance=9
 Require ($e.TryActivatePersonEncounter($room,$layout) -and $e.PersonEncounterActive -and $e.Finale -and $e.FinalActionsAllowed) "Mapped $kind target must activate before global clearance."
 Require (-not $e.TryActivatePersonEncounter($room,$layout)) 'Encounter activation must latch without repeating.'
}
$e=[Encounter]::new();$step=[Step]::new();$step.Target=7;$step.Action=[FindPersonAction]::new();$e._steps.Add($step)
$live=[Dynel]::new();$live.Identity=9;[DynelManager]::Live=$live
Require ($e.TryActivatePersonEncounter($room,$layout)) 'Previously unknown target room must activate on exact live containment.'
[DynelManager]::Live=$null
$e=[Encounter]::new();$e._steps.Add($step);$e._rooms.Add(7,9);$e.Preflight=$false
Require (-not $e.TryActivatePersonEncounter($room,$layout) -and $null -ne $e.Failure -and -not $e.PersonEncounterActive) 'Preflight failure must preserve protection and report failure.'
$e=[Encounter]::new();$item=[Step]::new();$item.Action=[FindItemAction]::new();$item.Target=7;$e._steps.Add($item);$e._rooms.Add(7,9)
Require (-not $e.TryActivatePersonEncounter($room,$layout)) 'Other objective types must retain their existing activation policy.'
Write-Output 'PASS production C# encounter activation handles mapped/live FindPerson and KillPerson, latches and preserves preflight/other-objective gates'
foreach($text in @('_objective.FinalActionsAllowed = _objective.PersonEncounterActive || allRoomsChecked',
 '_objective.TryActivatePersonEncounter(encounterRoom, _layout)',
 '_objective.PersonEncounterActive && _objective.HasFindPersonTarget',
 'Room next = RouteTo(current.Instance, id => ordinary(id) && !reserved.Contains(id), true);',
 'next = RouteTo(current.Instance, ordinary, false);',
 'if (_clearedRooms.Count < Playfield.Rooms.Count)',
 'if (_loot.UnfinishedMissionLootCount > 0)')) {
 Require ($dungeon.Contains($text)) "Missing encounter/order/exit invariant: $text"
}
Require ($objective.Contains('step.Action is FindPersonAction && (!RewardConfirmed || !FinalActionsAllowed)')) 'FindPerson protection must remain until acknowledged completion.'
Require ($objective.Contains('FindPersonObservationConfirmation = TimeSpan.FromSeconds(5)')) 'FindPerson observation must retain its five-second proof requirement.'
$encounter=$dungeon.IndexOf('if (_objective.PersonEncounterActive && _objective.HasFindPersonTarget')
Require ($encounter -lt $dungeon.IndexOf('if (_readiness.InCombat)', $encounter)) 'Encountered FindPerson must retain observation ownership under exact-target aggro.'
Write-Output 'PASS soft room preference, encounter ownership, persistent activation and remaining-clearance/loot exit gates'
