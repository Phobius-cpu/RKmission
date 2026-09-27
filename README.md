# RKMission

AO# plugin for solo Rubi-Ka missions. The source targets .NET Framework 4.8 and
uses the AOSharp SDK packages referenced in `RKmission/RKmission.csproj`.

## Run

1. In the desired Rubi-Ka playfield, use a solo mission terminal. RKMission
   remembers the terminal you used.
2. Set the target playfield with `/rkm zone <playfield id>` (the current
   playfield is the default), then use `/rkm start`.
3. RKMission first chooses the nearest already accepted mission in that
   playfield. If there is none, it requests mission offers until the playfield
   matches, accepts the nearest matching offer, and travels to its entrance.
4. Inside, it generates a dungeon navmesh, clears hostiles in the current room,
   handles a visible mission objective, loots corpses and chests, lockpicks
   locked chests and doors, then follows room connections to an uncleared room.
   The run stops when every reachable room is clear.

Commands: `/rkm start`, `/rkm stop`, `/rkm status`, `/rkm zone <id>`,
`/rkm rolls <count>` (default 100), and `/rkm difficulty <0-255>` (default 128).

Keep a Lock Pick in normal inventory and enough free slots for loot. RKMission
stops rather than skipping a locked object, an unreachable enemy, or a failed
container. It also stops if movement stalls, the character dies, or the
objective cannot be found in the explored rooms.

The current AO# navigation path covers the selected outdoor playfield and the
mission dungeon. If a chosen mission is in another playfield, move the
character to that playfield; travel to the mission resumes there. The plugin
reports when it has cleared all reachable rooms, but AO# mission reward and
quest completion should be checked in game. It does not delete missions or
automatically start another run.

## Source decisions

- Mission requests and acceptance follow the supplied Mali mission roller:
  `MissionTerminal.RequestMissions`, `Mission.RollListChanged`, and
  `CreateQuestMessage`.
- Room connections and navmesh generation follow the supplied AOSharp.NewBots
  DungeonSolver/DungeonRunner pattern. The runner clears a room before
  selecting its next connected room.
- Corpse/chest opening and item transfer follow the supplied Manager.Loot
  pattern: `Inventory.ContainerOpened`, `Container.Items`, and
  `Item.MoveToInventory()`.
- Locked objects use AO# `LockableItem`/`Door` state and `Lock Pick.UseOn`.
- The uploaded `RKmission.zip` contained an earlier, separate dungeon bot
  experiment. The old repository held disconnected placeholders and duplicate
  classes; these were replaced with one AO# plugin entry point.

The user compiles and tests in AO#. Local compilation and tests are
intentionally left to that in-game workflow.
