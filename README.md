# RKMission

AO# plugin for solo Rubi-Ka missions. The source targets .NET Framework 4.8 and
uses the AOSharp SDK packages referenced in `RKmission/RKmission.csproj`.

## Run

1. Stand within 7.5 m of a solo mission terminal and use it. RKMission can
   also find a nearby terminal when a use event was missed.
2. In the RKMission window, set the target playfield, roll limit, and difficulty,
   then press **Start rolling / run**. The current playfield is the default.
   Use `/rkm` to reopen the window.
3. RKMission first chooses the nearest already accepted mission in that
   playfield. If there is none, it requests mission offers until the playfield
   matches, accepts the nearest matching offer, and travels to its entrance.
4. Inside, it generates a dungeon navmesh and displays the room walls and doors
   from Mali's Dungeon Map 2.0. It clears hostiles in the current room, handles
   a visible mission objective, opens corpses and chests, lockpicks locked
   chests and doors, then follows the displayed room graph. A stalled route is
   marked blocked and the closest reachable room is chosen next.

The window also has **Stop**, status, and a Manager.Loot-style item list. Chat
commands remain available as a fallback: `/rkm start`, `/rkm stop`, `/rkm status`, `/rkm zone <id>`,
`/rkm rolls <count>` (default 100), `/rkm difficulty <0-255>` (default 128),
`/rkm loot list`, `/rkm loot add <name or item ID>`, and
`/rkm loot remove <number>`.

Loot is an allowlist. It begins empty, so RKMission opens containers but takes
only selected items. Rules are stored per character at
`%LOCALAPPDATA%\AOSharp\RKmission\<character>\loot-rules.json`. The JSON uses
Manager.Loot fields `Name`, `Lql`, `Hql`, `Quantity`, `Exact`, and `OneEach`.
Use the window to add names or IDs with QL, quantity, exact-name, and one-each
settings, and to remove selected rules. The add chat command creates a broad
QL 1-500 rule.

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

- Mission requests and acceptance follow the supplied Mali mission roller:
  seven slider arguments to `MissionTerminal.RequestMissions`, neutral values
  encoded as 255, `Mission.RollListChanged`, and `CreateQuestMessage`.
- The wall geometry and door display come from the supplied Mali Dungeon Map
  2.0 source. The copied map drawing helper is included with the required
  two-dimensional line renderer and angle conversion. The same room connections drive breadth-first navigation and
  nearest-room fallback. AO# still supplies the movement navmesh.
- Corpse/chest opening, selective item transfer and rule fields follow the
  supplied Manager.Loot source. `Inventory.ContainerOpened` and
  `Item.MoveToInventory()` are used for each chosen item.
- Locked objects use AO# `LockableItem`/`Door` state and `Lock Pick.UseOn`.
- The uploaded `RKmission.zip` contained an earlier, separate dungeon bot
  experiment. The old repository held disconnected placeholders and duplicate
  classes; these were replaced with one AO# plugin entry point.

The user compiles and tests in AO#. Local compilation and tests are
intentionally left to that in-game workflow.
