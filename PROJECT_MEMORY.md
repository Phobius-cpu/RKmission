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
- No object or enemy is silently skipped after a failed interaction: the run
  stops and reports the reason for the user's in-game diagnosis.
- Navigation can resume in the target outdoor playfield but does not route
  automatically across playfields. Objective completion/reward status requires
  in-game confirmation; the plugin reports room clearance separately.
- User retains compilation and in-game testing responsibility. Do not run
  local builds/tests unless they request it.
