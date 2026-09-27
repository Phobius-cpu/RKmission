# RKMission Project Memory

_Last updated: 2026-09-27_

This file is a durable project-context summary for future RKMission development sessions.

## Project

- **Name:** RKMission
- **Repository:** `Phobius-cpu/RKmission`
- **Game:** Anarchy Online
- **Automation framework:** AO# / AOSharp
- **Primary target:** Rubi-Ka missions

## Intended Bot Workflow

RKMission is intended to automate the full Rubi-Ka mission loop:

1. Roll missions.
2. Select/accept a suitable mission.
3. Travel to the mission location and enter the mission dungeon.
4. Explore the dungeon systematically.
5. Kill hostile enemies encountered.
6. Detect and open normal doors.
7. Use lockpicking for locked doors.
8. Detect treasure chests/containers.
9. Lockpick locked containers where required.
10. Open/loot mission treasure containers.
11. Continue exploration until the mission is cleared/completed.
12. Exit and repeat the mission cycle.

## Reference Source Archives Supplied

The user supplied these archives as implementation/reference material:

- `RKmission.zip` — main RKMission project.
- `aosharp.newbots-master.zip`
- `aosp.knows-aosharp-mods-master.zip`
- `aosp-bots-master.zip`
- `malis-dungeon-map-2.0-master.zip`
- `malis-mission-roller-2.0-main.zip`

Reference implementations should be used to understand AO# APIs, mission rolling, mission/dungeon mapping, navigation, combat integration, door/container interactions, and lockpicking.

## Development Workflow

- ChatGPT should focus on source inspection, design, and code changes.
- **Do not spend time compiling or testing locally unless explicitly requested.**
- The user will pull changes, compile them, test them in-game, and report compiler errors, AO# exceptions, logs, navigation problems, or gameplay behavior.
- Follow-up changes should be based on those user test results.

## GitHub as Project History

Use this repository as the persistent source of truth for RKMission project code and project-history notes.

Project-relevant memory and visible conversation summaries may be recorded here. Hidden system instructions, hidden reasoning, private chain-of-thought, credentials, or other non-user-visible internal data must not be stored.

## Current Implementation (2026-09-27)

- Replaced the starter's duplicate, nonfunctional portable stubs with a single
  AO# plugin entry point, `RkMissionBot`, targeting .NET Framework 4.8.
- `MissionRoller` filters offered and accepted missions by configured Rubi-Ka
  playfield and selects the nearest one when the character is in that area.
  It requests offers through `MissionTerminal` and accepts through
  `CreateQuestMessage`, with a roll limit and acceptance timeout.
- `MissionDungeon` generates an AO# dungeon navmesh and uses room connections
  to clear rooms in order. It prioritizes enemies, mission objective
  interaction, corpse/chest loot, locked object handling, and movement to
  another uncleared room.
- Failed object/enemy interactions stop with a reason for in-game diagnosis.
  Stalled room routes now trigger the graph fallback described below.
- Navigation can resume in the target outdoor playfield but does not route
  automatically across playfields. Objective completion/reward status requires
  in-game confirmation; the plugin reports room clearance separately.
- User retains compilation and in-game testing responsibility. Do not run
  local builds/tests unless they request it.

## Latest In-Game Feedback and Source Changes (2026-09-27)

- The user reported that mission rolling did not start and dungeon navigation
  stopped with `Navigation stalled; stopped to avoid skipping a room.`
- Mission rolling now uses the seven-argument `RequestMissions` call from
  `malis-mission-roller-2.0-main.zip`, including 255 for the six neutral
  sliders. It waits for `Mission.RollListChanged` before another request,
  retries after a response timeout, and finds a nearby mission terminal if
  the terminal-use event was missed. Zone filtering and mission acceptance
  still apply.
- The wall/door geometry from `malis-dungeon-map-2.0-master.zip` is included
  in the plugin. A displayed room graph is built from the same dungeon rooms
  and door connections used to navigate. The bot clears the current room
  before traversing one graph edge at a time. On movement stall it blocks
  that edge and chooses the closest reachable adjacent route to an unvisited
  room. If blocked paths leave rooms inaccessible, it reports that rather
  than claiming the mission is fully clear.
- Loot now uses a per-character allowlist adapted from the `Manager.Loot`
  `Name`/QL/quantity/exact/one-each rule pattern in `aosp-bots-master.zip`.
  `/rkm loot add|remove|list` edits the list; the JSON under local AOSharp
  app data supports finer rules. Unselected items stay in containers.
- `AOSharp.NewBots.sln` was removed from the repository.
- These changes are source-only. The user will compile and test in-game and
  report any compiler errors or behavior that needs iteration.

## Compile Inspection Before Embedding (2026-09-27)

- The user could not compile the previous revision. A subsequent `main` commit
  supplied the missing drawing helpers, removed the unused AOSharpSDK.Nav
  package, and added an interim RKMission window. The embedded original map
  includes its own `MDebug`/`MathExtras`; the interim helpers and window were
  removed with their substitute classes during this integration.

## Embedded Plugin Architecture and Doorway Fix (2026-09-27)

- The user requires the original plugin code and interfaces, rather than RKMission
  lookalikes. The source trees from `malis-mission-roller-2.0-main.zip`,
  `malis-dungeon-map-2.0-master.zip`, and `aosp-bots-master.zip` Manager.Loot are
  embedded under `RKmission/Plugins` with their namespaces, UI XML, textures,
  sounds, JSON databases, and rule model. Their archive project/solution files
  and duplicate assembly attributes were excluded from the one RKmission project.
  Their original public `Run`/UI APIs remain callable; only RKmission derives
  from `AOPluginEntry` so AO# has one unambiguous entry point in the assembly.
- RKMission invokes `MaliMissionRoller2.Main` and `MainWindow` for offers and
  acceptance. A small `StartZoneRolling` addition selects the nearest offer in
  the configured zone while the original slider UI and `/mmr` remain available.
- RKMission invokes `MalisDungeonMap2.DungeonMap` for the original map renderer,
  `/mapsettings`, and config UI. The local duplicate wall renderer was removed;
  `DungeonLayout` retains only AO# room adjacency for routing.
- RKMission invokes `ManagerLoot.ManagerLoot` for its original rules, settings UI,
  corpse/chest lockpicking, and item movement. It sets the active room and walks
  into range, while Manager.Loot's own state machine performs looting. The old
  `LootRules` substitute and `/rkm loot add/remove` were removed. Use
  `/ManagerLoot` to select items. Rules remain in Manager.Loot's own per-character
  or shared folder format.
- The prior room loop was caused by accepting any instantaneous room change at
  a doorway and resetting the target, while progress was measured by character
  position even without a confirmed room entry. A room ID can flicker across a
  threshold and the character can move without crossing it. The bot now keeps a
  target until the destination room ID is stable for one second, gives each edge
  18 seconds regardless of position movement, blocks failed edges for the run,
  and finds the next closest reachable unvisited room without returning to that
  doorway. It clears each confirmed room before choosing another edge.
- No local compile or tests were run, per the user's workflow. The next validation
  is the user's build and in-game room-entry, map, rolling, and loot checks.

## Dedicated Room Crossing Revision (2026-09-27)

- The previous doorway fix still navigated to `Room.Center` after opening a
  door. Mali's `DungeonTerrainHeight` uses `Room.Center` in tilemap units
  multiplied by tile size, while AO# movement destinations and Mali's wall
  vertices are world positions. The earlier destination could therefore be
  wrong even when room ID stabilization worked.
- `DungeonLayout` now calls the embedded original
  `MalisDungeonMap2.DungeonMapFactory.GetDungeonData()`, converts its
  recentered room boundary edges back to world coordinates, and samples a
  safe target interior point and a deeper fallback. It retains AO#'s original
  room door links and door objects for topology and interaction.
- `MissionDungeon` now owns one transition state from source room to target:
  approach the connecting doorway, open or lockpick it, cross to the Mali
  interior point, and push deeper if navigation arrives while AO# still reports
  the source room. It suppresses ordinary room, combat, objective, and loot
  routing until the target room ID remains stable for a second and the player
  is safely inside the Mali target outline.
- Confirmed crossings mark the target visited and hold the reverse edge for
  eight seconds. Failed crossings log retries and temporarily blacklist the
  undirected edge for 30 then 90 seconds, choosing another reachable unvisited
  room; a third failure blocks it for the run. Missing safe Mali geometry is
  logged. Existing Manager.Loot and mission roller integrations remain in place.
- Source only. No local compilation or tests were performed; the user will
  pull, compile, and test in game.

## Namespace Collision Reported by User (2026-09-27)

- The user reported a compiler ambiguity between
  `MaliMissionRoller2.MainWindow` and `MalisDungeonMap2.MainWindow`.
  `RkMissionBot.cs` imported both namespaces and used the unqualified
  `MainWindow.CurrentTerminal` twice. Both plugin sources define that class,
  but only the mission roller class contains `CurrentTerminal`.
- Both references now explicitly use `MaliMissionRoller2.MainWindow`.
  A source scan found `RkMissionBot.cs` to be the only file importing both
  namespaces. No local build or test was run under the user's workflow.

## Blocked Corpse and Spawned Sentry Feedback (2026-09-27)

- The user observed the bot stopping near a corpse behind an obstruction and
  not targeting an Alarm Sentry spawned by a security camera.
- `LootInRoom` previously checked its 20-second failure timer only after
  reaching within 4.5m; the out-of-range branch could navigate forever.
  It now picks a standoff point within the original Manager.Loot interaction
  radius, tries one alternate point if progress stops, and skips an unreachable
  object after bounded attempts. A narrow RKMission hook keeps skipped objects
  out of both Manager.Loot's room candidate list and its own opening loop for
  the rest of the mission. Skips are logged and summarized.
- The combat filter previously excluded every `IsPet` entity and required a
  non-null room match. It now considers nearby spawned entities owned by an
  enemy in the current room, nearby attackers, and Alarm Sentries even when
  room assignment is temporarily missing. Player characters and the local
  player's pets remain excluded. A newly selected enemy interrupts a corpse
  route immediately, and target selection is logged.
- Source only; no local compilation or in-game tests were run.

## Door Lockpicking and Room Containers (2026-09-27)

- The user reported repeated lockpick attempts on visibly open doors, no
  successful Lock Pick use on locked doors, and ignored lootable containers.
- AO# exposes `Door.IsOpen` and `Door.IsLocked` as item flags. RKMission
  previously trusted those flags immediately and passed `door.Identity` to
  `Item.UseOn`. The embedded AO# examples use the `Door` object itself.
  Transitions now refresh the door from the playfield, probe traversal for
  three seconds before lockpicking a door flagged locked and closed, approach
  into interaction range, and call `pick.UseOn(door)`. The log includes the
  selected door identity and flags. If room links do not identify a physical
  door, the layout also tries the nearest door to Mali's threshold position.
- Manager.Loot and RKMission previously required `Dynel.Room.Instance` to
  match the current room before considering a corpse or container. Both now
  accept Mali's original world-space room outline as a fallback for objects
  whose AO# room association is missing or mismatched. During a mission,
  Manager.Loot no longer auto-disables solely because its item rule list is
  empty; it can still open a room container. Item movement still follows the
  user's Manager.Loot rules. Container candidates are logged by RKMission.
- Source only; the user will compile and test in game.

## Target Room Detection During Crossing (2026-09-27)

- The user supplied a log for transition 2->6 where AO# already reported room
  6, but RKMission printed `still in room 6; pushing deeper` before confirming.
- The crossing stall timer was allowed to trigger regardless of which room was
  detected. The target-room stability timer also began only after the player
  met the stricter interior clearance check, adding another second of delay.
- RKMission now starts the stability timer as soon as the target room is
  detected. It confirms only after the same one-second stable reading and
  safe Mali interior check, and holds that safe position while waiting.
  When detection reports the target near its doorway, it continues inward
  without incrementing source-room retries; one deeper destination is used
  only if the interior approach stalls. Source-room retries remain bounded.
- Combat and loot still start only after confirmed entry. No local compile
  or in-game test was run.
