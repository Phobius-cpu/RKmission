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

## Compile Inspection and Interface Follow-up (2026-09-27)

- The user could not compile the previous revision and requested a static source
  inspection. The copied Mali dungeon-map files referenced `MDebug.DrawLine`
  and `MathExtras.Rad2Deg`, but the defining helper file was omitted. RKMission
  now includes the needed map line renderer and angle constant in its own map
  source. The drawing helper uses the same AO# debugger entry point and default
  scale as the reference map.
- Checked mission, inventory, room, door, and SharpNav signatures against the
  referenced AOSharpSDK 1.0.106 and AOSharpSDK.SharpNav 1.0.44 APIs. The
  `SetNavDestination`, `LoadNavmesh`, and `GenerateNavMeshAsync` calls are valid.
- Removed the unused AOSharpSDK.Nav package reference. The map and movement
  code uses AOSharpSDK.SharpNav; no stale solution file remains in the tree.
- Added an AO# window adapted from Mali's rolling controls and Manager.Loot's
  rule editor. It covers playfield, difficulty, roll limit, start/stop, status,
  and per-character loot add/remove with QL, quantity, exact, and one-each
  settings. `/rkm` reopens it; chat subcommands remain fallback controls.
- No local build or in-game test was run. The user will pull, compile, and
  test in AO#.
