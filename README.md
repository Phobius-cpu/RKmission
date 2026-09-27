# RKMission

AO# plugin for solo Rubi-Ka missions. The source targets .NET Framework 4.8 and
uses the AOSharp SDK packages referenced in `RKmission/RKmission.csproj`.

## Run

1. Stand within 7.5 m of a solo mission terminal and use it. RKMission can
   also find a nearby terminal when a use event was missed.
2. Set the target playfield with `/rkm zone <playfield id>` (the current
   playfield is the default), then use `/rkm start`.
3. RKMission first chooses the nearest already accepted mission in that
   playfield. If there is none, it requests mission offers until the playfield
   matches, accepts the nearest matching offer, and travels to its entrance.
4. Inside, it generates a dungeon navmesh and starts Mali's original Dungeon
   Map 2.0 renderer. It clears hostiles in the current room, handles a visible
   objective, walks within range of corpses and chests for Manager.Loot to
   process, lockpicks locked doors, then follows AO# room connections. A room
   change must remain confirmed for one second; failed connections are blocked
   and the closest reachable unvisited room is chosen next.

Commands: `/rkm start`, `/rkm stop`, `/rkm status`, `/rkm zone <id>`,
`/rkm rolls <count>` (default 100), `/rkm loot`, and `/rkm map`.

Use `/ManagerLoot` for the original item list and settings, `/mmr` for Mali's
roller settings, and `/mapsettings` for the original map settings. Manager.Loot
keeps its own per-character or shared JSON lists under
`%LOCALAPPDATA%\AOSharp\ManagerLoot`.

Keep a Lock Pick in normal inventory and enough free slots for loot. RKMission
stops for a locked object without a pick, an unreachable enemy, or a failed
container. If a route stalls, it tries the next reachable room and reports
when blocked routes leave rooms unreachable. It stops if the character dies or
the objective cannot be found in the explored rooms.

The current AO# navigation path covers the selected outdoor playfield and the
mission dungeon. If a chosen mission is in another playfield, move the
character to that playfield; travel to the mission resumes there. The plugin
reports when it has cleared all reachable rooms, but AO# mission reward and
quest completion should be checked in game. It does not delete missions or
automatically start another run.

## Source decisions

- The original Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot
  source trees and UI/config assets are embedded in `RKmission/Plugins`.
  RKMission invokes their classes directly; the small additions set a target
  rolling zone and limit Manager.Loot to the current room.
- AO# supplies the room graph and movement navmesh. The original Mali map
  renderer supplies the dungeon display. Manager.Loot owns corpse/chest
  opening, lockpicking, list matching, and selected item transfers.
- Locked objects use AO# `LockableItem`/`Door` state and `Lock Pick.UseOn`.
- The uploaded `RKmission.zip` contained an earlier, separate dungeon bot
  experiment. The old repository held disconnected placeholders and duplicate
  classes; these were replaced with one AO# plugin entry point.

The user compiles and tests in AO#. Local compilation and tests are
intentionally left to that in-game workflow.
