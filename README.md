# RKMission

AO# plugin for solo Rubi-Ka missions in Anarchy Online. It combines the original
Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot interfaces with
mission travel, room exploration, combat targeting, door handling, and looting.

**Status (2026-09-28):** the user reports that the bot seems good for now after
the navigation and compiler fixes. This does not establish coverage of every
mission layout.

## Setup

- Use the classic client: the embedded Manager.Loot rejects the new engine.
- Compile `RKmission/RKmission.csproj` yourself. It targets .NET Framework 4.8
  and references AOSharpSDK, AOSharpSDK.SharpNav, and Newtonsoft.Json.
- Deploy the compiled output and dependencies through your AO# setup. Preserve
  the `Plugins/MaliMissionRoller2`, `Plugins/MalisDungeonMap2`, and
  `Plugins/ManagerLoot` folders beside `RKmission.dll`. The project copies
  their required JSON, UI, texture, and sound assets.
- Supply AO# outdoor navmeshes in the deployed plugin's `NavMeshes` folder
  for travel within the selected playfield. They are not included here.
  Dungeon navmeshes are generated on entry.
- Keep a **Lock Pick** in normal inventory, enough lockpicking skill, and
  free inventory or configured backpack space.
- Open `/ManagerLoot` to choose loot rules. Review its reverse and delete
  settings; enabling Delete can remove items left in containers.

## Quick start

1. Stand within 7.5 m of a solo mission terminal and use it.
2. Set difficulty and sliders in Mali's roller window, which opens when
   RKMission loads. Use its Settings button for the original options.
3. Use `/rkm zone <playfield id>`. The default is the playfield where the
   plugin was loaded. Optionally change the 100-roll limit with `/rkm rolls <count>`.
4. Use `/rkm start`. An accepted mission in the target playfield takes
   priority; otherwise it rolls and accepts a matching offer.
5. Travel to the mission's playfield yourself if necessary. Navigation resumes
   there and attempts to use the mission entrance.
6. Inside, the bot clears rooms, interacts with visible objectives, processes
   room loot, and crosses doors. Check `/rkm status` or stop with `/rkm stop`.
7. After clearance is reported, check the game's objective/reward, exit
   yourself, and start another run when ready.

You can also start while already inside a mission. Keep the correct target
playfield set to associate an accepted mission with its objective.

Zone rolling accepts offers by playfield and distance, using the target
playfield's origin when rolling from another playfield. It bypasses the
roller's manual reward-item and mission-type matching filters; difficulty
and slider controls still apply.

## Commands

| Command | Purpose |
| --- | --- |
| `/rkm` or `/rkm status` | Show running/rolling state, target zone, selected mission, and visited/cleared room counts. |
| `/rkm zone <id>` | Set a positive Rubi-Ka playfield ID before starting. |
| `/rkm rolls <count>` | Set a positive roll limit (default 100). `/rkm rolls` displays it. |
| `/rkm start` | Start selection, rolling/travel, or dungeon exploration. |
| `/rkm stop` | Stop RKMission, its rolling, and movement. |
| `/rkm loot` | Show guidance to use `/ManagerLoot`; does not open a window. |
| `/ManagerLoot` | Open the original loot rule list and settings. |
| `/lm` | Toggle Manager.Loot's independent enable state. |
| `/printitems` | Toggle Manager.Loot's item-printing option. |
| `/rkm map` or `/mapsettings` | Toggle Mali's map settings window; configure map visibility there. |
| `/mmr maxitems <count>` | Change the roller's displayed-item limit. |
| `/mmr shopvalue <value>` | Change the roller's shop-value factor. |

Bare `/mmr` has no action in this embedded version. Configure rolling through
its window. RKMission enables Manager.Loot as needed for a room; it can remain
independently enabled after a run if you enabled it yourself. `/lm` toggles that state.

## Current behavior and recent fixes

- Mali's world-space room outlines provide safe interior waypoints and map
  visible enemies, corpses, and containers to rooms. Discovery follows what
  the client has loaded or spawned.
- The closest usable adjacent unvisited room takes priority. Otherwise the
  bot follows the shortest available chain through visited rooms to another
  unvisited room, logging intermediate and goal rooms.
- A dedicated crossing approaches the mapped threshold, resolves a live door
  within 3 m, opens/lockpicks as needed, then continues inside. Locked/closed
  flags first trigger a passage probe; a distant door identity cannot redirect it.
- Entry requires 500 ms of stable target-room detection and a safe interior
  position. Combat, loot, and normal room selection resume after confirmation.
  The reverse connection has an eight-second cooldown.
- Failed crossings block a connection for 30 then 90 seconds; a third failure
  blocks it for that run. Other reachable routes are considered.
- Combat interrupts loot approaches. Nearby attackers, hostile spawned
  entities, and Alarm Sentries are considered alongside room enemies;
  players and the local player's pets are excluded.
- Manager.Loot handles container opening, chest lockpicking, rules, and item
  transfers. A blocked loot approach gets an alternate attempt, then an
  unreachable object is skipped with a log for that run.
- The `adjacent` CS0136 compile conflict in `MissionDungeon.NextRoom` was
  fixed using distinct target and traversal names without changing behavior.

## Settings and troubleshooting

Manager.Loot keeps character lists under
`%LOCALAPPDATA%\AOSharp\ManagerLoot\<character>` and shared lists under
`%LOCALAPPDATA%\AOSharp\ManagerLoot\Shared`. These personal runtime settings
are separate from the source backup. Roller/map settings remain in their
deployed plugin folders.

- **No rolling:** use a nearby terminal, check the target zone and roll limit,
  then start again.
- **Map missing:** the original map recommends the launcher's
  `Direct 3D T&L HAL` graphics setting.
- **Door failure:** check the transition log, Lock Pick, and skill. Temporary
  route blocks may expire and allow another attempt.
- **Skipped loot/items left behind:** check skip logs, rules, and free space.
  An empty rule list can still open containers; transfers follow loot settings.
- **Stopped run:** read the reason in chat, including death, unreachable
  enemies, failed routes/missing geometry, missing objective items or objectives,
  and AO# exceptions.

The bot does not route across playfields, automatically exit, delete accepted
missions, or begin another cycle. Room clearance does not confirm the game's
quest completion or reward.

## History and local backup

`PROJECT_MEMORY.md` stores durable project context and `CONVERSATION_LOG.md`
stores user-visible conversation summaries. GitHub `main` is the source of truth.
The synchronized local source backup is at:

    C:\Users\Sumiko\OneDrive\Desktop\RK Mission Proj

It contains source, embedded assets, documentation, and Git history. It does
not include a newly compiled build or personal runtime settings. The user
compiles and tests in AO#; no local compilation or tests were run for this update.
