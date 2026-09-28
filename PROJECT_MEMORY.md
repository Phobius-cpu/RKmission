# RKMission Project Memory

_Last updated: 2026-09-28_

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

## Interior Arrival Confirmation (2026-09-27)

- The user supplied a transition 1->5 log showing target-room detection followed
  by `room detected near doorway; moving farther inside` before confirmation.
- Mali's sampled interior destination can be only 2.5m from the door and 0.8m
  from a wall. Confirmation required the player's position to exceed those
  same limits, so navigation arrival tolerance could leave the character in
  the target room but just short of the stricter confirmation region. The
  crossing branch also treated proximity to the interior destination as a
  reason to push deeper.
- Confirmation now accepts a stable target-room reading after 500ms when the
  player is at least 1.5m past the threshold and 0.4m inside Mali's room
  outline. The active interior route keeps moving during that brief check.
  A deeper route is selected only if navigation ends or stalls before this
  safe entry condition is met. Source-room retries and the reverse-edge
  cooldown remain in place.
- Source only; the user will compile and test in game.

## Silent Door Approach After Reaching Threshold (2026-09-27)

- The user supplied a transition 1->3 log with `door reached` followed by a
  30-second timeout and no probe, open, lockpick, or crossing log.
- In `OpenDoor`, the only silent loop after `door reached` was the range branch:
  it checked `Door.DistanceFrom(player) > 4.5f` and repeatedly navigated to
  the mapped threshold, even though the preceding phase used world-space
  distance to that threshold. The old log has no distances, so the precise
  AO# range discrepancy cannot be measured retrospectively.
- Door interaction now uses world-space distance to the refreshed door
  position and approaches that same live position if needed. A stalled door
  approach fails in six seconds with its distance in the log, rather than
  silently consuming the full transition timeout. The `door reached` log now
  includes threshold, live door, and AO# distances for in-game diagnosis.
- Source only; the user will compile and test in game.

## Adjacent Room Priority and Spatial Door Matching (2026-09-27)

- The user reported apparent random room selection and travel toward the
  origin. A transition 2->7 log showed the player 2.3m from the planned
  threshold but 78.3m from the `Door` refreshed by identity.
- The layout previously stored a `Door` object and its position as the
  threshold, then looked up that identity later without checking position.
  The live door could be far from the originally mapped passage, causing
  navigation to leave the local room. The threshold is now always the
  `Room.GetDoorPosRot` position used by Mali's original map, and each tick
  chooses only a live door within 3m of that threshold, preferring matching
  room links. A far identity cannot redirect the character.
- `NextRoom` now explicitly takes the closest usable adjacent *unvisited*
  room before any graph search. When none remains, breadth first search
  routes through visited rooms toward an unvisited room and logs both the
  intermediate room and target. Equal-depth fallback paths order their
  doorways from the entry position of each room.
- Source only; the user will compile and test in game.

## Malis Room Entity Survey (2026-09-28)

- The user additionally requested use of Malis Dungeon Map resources to find
  lootable containers and enemies throughout the mission.
- The original renderer classifies live `DynelManager.AllDynels` entries by
  `IdentityType.SimpleChar` and `IdentityType.Container`; it does not provide
  an inventory of unseen or unspawned entities. RKMission now shares that
  live source and Mali's world-space room outlines through `DungeonLayout`.
- Combat considers room-mapped `SimpleChar` entries even when AO# room tags
  are missing or inaccurate, while preserving player and own-pet exclusions
  and spawned-enemy targeting. Manager.Loot's original state machine receives
  the same room-mapped candidates for container and corpse selection and
  opening. Mission objectives also use the common room membership check.
- A one-time room survey log reports visible live enemy candidates,
  containers, and corpses; new spawns are still evaluated on later ticks.
  Mali wall outlines are cached for repeated entity checks.
- Source only; no local compile or in-game test was run.

## Adjacent Local Scope Compile Fix (2026-09-28)

- The user reported C# CS0136 after the adjacent-room priority patch.
  `MissionDungeon.NextRoom` declared both a method-scope nullable `adjacent`
  target and a nested `foreach` variable with the same name.
- Renamed them to `adjacentTarget` and `adjacentCandidate`, including all
  references. Room selection, graph traversal, and log output are unchanged.
  Source inspection found no other similar conflicts in `NextRoom` or the
  surrounding transition methods.
- No local compilation or tests were run; the user will pull and compile.

## Working State, Usage Guide, and Local Backup (2026-09-28)

- After the CS0136 fix, the user reported that the bot seems good for now.
  This is user feedback, not verification of every mission layout.
- The user requested a local source backup at
  `C:\Users\Sumiko\OneDrive\Desktop\RK Mission Proj`, an updated README
  on GitHub and locally, and saved project memories/conversation summaries.
- The README now covers setup, quick start, verified commands, current
  room/door navigation, spawned enemies, bounded loot approaches, settings,
  and troubleshooting. It corrects crossing confirmation to 500ms, explains
  map settings and informational `/rkm loot`, and distinguishes the roller UI
  from `/mmr maxitems` and `/mmr shopvalue`. Zone rolling uses zone/distance
  acceptance rather than manual reward/type filters.
- The local backup contains synchronized main source, embedded assets,
  documentation, and Git history. Personal runtime settings remain separate.
  No new binaries were built; no local compilation or tests were run.
