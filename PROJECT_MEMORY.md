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

The current responsibility boundary supersedes earlier automatic rolling plans:

1. The user rolls/selects/accepts any number of Rubi-Ka missions and travels
   between outdoor playfields by any means.
2. `/rkm start` arms local takeover. RKMission tracks all resolved accepted
   Rubi-Ka destinations, independent of Mali roller filter settings.
3. On reaching a playfield with accepted missions, capture one player origin,
   estimate horizontal entrance distances, choose nearest (mission ID breaks
   ties), and build only that entrance's route in the active movement mode.
   Keep the chosen identity/entrance fixed during movement. Estimates are not
   certified complete paths; other missions receive no route/probe planning.
4. Ground uses complete navmesh paths when available, otherwise bounded AO#
   direct local waypoints. Flying commits a complete waypoint sequence to
   within 2 m of the chosen entrance, selects/aligns entrance height, then
   proceeds to precise entrance approach and interaction in the flying vehicle. AO# flight state
   selects automatically; `/rkm travel auto|ground|flying` overrides by session.
5. Flight has no dismount prompt/wait or forced ground approach. Both modes
   converge on the chosen entrance, a unique live door when exposed or a
   proximity entry attempt, and exact mission/dungeon verification. Refine
   entrance floor from nearby surface consensus/live door data. Prefer complete
   clear flight paths around/over objects; retain estimates when geometry is
   inconclusive. Follow the full waypoint list, then gate entry on near-entrance
   height alignment. Avoid speculative-probe hopping and frequent turn stops.
6. Existing `MissionDungeon` owns room exploration/navigation, combat, doors,
   lockpicking, and Manager.Loot. Preserve these working systems.
7. Preserve identity/action/target/completion metadata per accepted mission.
   Room clearance and removal are not quest completion. The user checks the
   in-game objective/reward and records it with `/rkm complete [bound id]`.
8. The user exits the dungeon. While armed, choose the nearest accepted mission
   from the new origin in that same playfield. If none remains, disarm and leave transport
   to the next playfield to the user; `/rkm start` re-arms there.

Automatic exit traversal and objective-solver rewrites are outside this pass.

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

## Historical Implementation (2026-09-27; rolling/travel superseded below)

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

## Accepted-Mission Local Travel Revision (2026-09-28)

The mesh requirements in this historical implementation are superseded by the
local-travel fix below.

- Replaced the RKMission front half's terminal/offer loop with `AcceptedMissions`.
  It polls accepted quests once per second, snapshots identity/name/outdoor
  playfield/entrance/dungeon identity/action types and objective target/item IDs,
  and retains removed or user-completed history for the plugin session. Geography
  follows the embedded Mali Rubi-Ka playfield catalog, without its user filters.
  Unresolved locations retry on later polls. RKMission never requests/accepts
  offers or deletes missions; the original roller window remains user-owned.
- `LocalRoutePlanner` evaluates both ground and flight alternatives for every
  local mission. Ground queries require a complete polygon corridor reaching
  the destination; cost includes endpoint approach. Flight candidates use eight
  nearby navmesh landing samples, terrain clearance/geometry corridor checks,
  and total climb/cruise/descent/final-ground distance. Both modes require the
  outdoor `NavMeshes/<playfield id>.nav` file. Missing/disconnected routes remain
  idle and retry, rather than selecting the first accepted mission.
- `LocalMissionTravel` defaults to AO# `MovementState.Fly` inference, with session
  command overrides. Flight steering/arrival includes altitude: SharpNav's
  ordinary waypoint arrival only checks X/Z, so it cannot implement descent
  safely by itself. Flight lands near the door, then waits for the user to
  dismount before the common ground approach. No vehicle equipment is changed.
- Doors must match accepted entrance coordinates, be unique, and remain the
  same live identity during entry. Use retries, stalls, and handoff waits are
  bounded. `Mission.FindMissionForCurrentDungeon` from pinned AOSharpSDK 1.0.106
  verifies exact quest identity and stable dungeon/room state before Start.
- Working dungeon logic is unchanged except `UpdateMissionBinding(Mission)`:
  the coordinator supplies a fresh live accepted quest or null after removal,
  avoiding access to a stale native quest pointer. Exploration, room crossings,
  combat, interior doors, lockpicking, and loot implementations are preserved.
- The pinned Mission/Quest interfaces expose no dependable reward/completion
  flag; QuestAction only defines Delete. Removal is `RemovedUnconfirmed`, never
  automatic success. `/rkm complete [bound mission id]` records user-confirmed
  completion for a verified run. Cleared-room and completion states are separate.
  Stop/start abandons a held binding and allows retry of an unfinished mission.
- After confirmation and user exit, re-cost remaining missions in that same
  outdoor playfield. Disarm when none remains, or if the user exits elsewhere.
  No automatic exit traversal or cross-playfield travel was added. No objective
  solving was rewritten. Completion history/mode are in-memory for this session.
- README documents `/rkm missions`, `/rkm travel auto|ground|flying`, and
  `/rkm complete [id]`; old zone/roll-limit commands now explain the new boundary.
  This pass does not refresh the earlier OneDrive/Desktop source backup.
- Source/API inspection only, including pinned SDK signatures. No compilation,
  package restore, automated tests, or in-game testing was performed. The user
  will pull main, compile, and validate takeover, route selection, flight landing,
  door/dungeon identity, and local continuation in game.

## In-Zone Local Travel Navmesh Dependency Fix (2026-09-28)

- User log: armed in the correct outdoor playfield with four accepted missions,
  but every candidate reported `ground=unreachable, flying=unreachable`, followed
  by a request to supply an outdoor mesh. Main `6e86c52` required
  `NavAgent.HasPathfinder` for ground costs and every flight landing point, then
  required a full mesh path for the landing-to-door leg. The common mesh gate
  explains this failure pattern; the log alone does not prove whether the mesh
  was absent or disconnected. Tight endpoint projection can also reject a mesh.
- Inspected the supplied AOSharp/newbots references and installed pinned
  AOSharpSDK.SharpNav 1.0.44 controller implementation. `SetDestination` queues
  a direct waypoint without checking a pathfinder; `SetNavDestination` invokes
  `SNavAgent` mesh path generation. Its bool confirms submission, not a queued
  path. `BTBotBase/Behaviors/BaseBehaviors.cs` uses the same no-mesh direct
  fallback. `Playfield.Raycast` uses the outdoor TilemapSurface interface.
- Retain complete-mesh ground costs when possible. Missing/disconnected mesh
  paths now receive direct world-distance estimates instead of discarding all
  missions. Ground travel/final door approach uses AO# `SetDestination` for
  up-to-12-m local steps, raycast surface/clearance checks, and limited side steps.
  Failed mesh submission or stalled mesh movement switches to direct fallback.
  These estimates/probes cannot guarantee a full path around large obstacles.
- Flight no longer queries a mesh for landing or final-leg cost. Eight nearby
  world-space approach points are sampled for terrain, rejecting steep faces
  and height discrepancies over 8 m. Verified terrain takes priority; missing
  distant hits allow a provisional flight approach, with a mandatory fresh
  suitable surface hit before descent. Cruise/climb/descent geometry checks
  remain, along with altitude-aware steering and the user's dismount step.
- Direct steps require progress within 12 seconds; ground has at most three
  obstruction/stall recoveries per mission. Flight has a 20-second target
  progress watchdog. Total local travel is capped at 15 minutes and dismount
  waits at two minutes; entrance/entry remains bounded at 60 seconds.
- Candidate logs explain mesh availability, actual movement state, position,
  costs and route reasons. Unchanged idle candidate logs are suppressed.
  Recovery/failure logs identify position/target and permit explicit restart
  after the user moves to a clearer approach. Removed misleading mandatory-mesh
  setup/recovery guidance in README and the coordinator's wait message.
- Changed only `LocalRoutePlanner`, `LocalMissionTravel`, the coordinator's
  outdoor wait text, and these three documents. `MissionDungeon`, accepted
  mission tracking, verified dungeon handoff, combat, room navigation, door,
  lockpick, and embedded loot/map/roller code are unchanged.
- Source/API review only. No compilation, package restore, tests, or in-game
  run. The user will pull main, compile, and validate ground/flight takeover,
  terrain landing, fallback stop behavior, and entry in game. The earlier
  Desktop/OneDrive backup is not refreshed by this request.

## Local Travel Soft Feasibility and Progressive Recovery (2026-09-28)

This section supersedes the clearance gates and three-recovery limits above.

- Latest user logs in outdoor playfield 665: no outdoor mesh, valid ground
  estimates around 32-100 m, but three side steps exhausted recovery. Every
  flight candidate was rejected with `no clear climb/cruise/descent to a
  suitable approach point` while the player was already in Fly state nearby.
- Main `07fdbfa`/travel implementation `cd74489` hard-gated flight selection on
  `TryCruise`'s three synthetic segments, stopped flight on any corridor hit,
  required a fresh terrain hit before descent, and counted ordinary ground
  detours against a lifetime three-recovery cap. Full 3D distance for door
  lookup also hid entrances with zero/stale accepted map height.
- Re-read cached pinned AOSharpSDK.SharpNav 1.0.44 controller/agent source and
  supplied AOSharp/newbots references. `SetDestination` queues a direct leg
  without a mesh; `SetNavDestination` submission alone does not prove a queued
  path. Path arrival zeroes Y and uses X/Z distance; flight steering retains
  explicit 3D rotation/arrival and a nondegenerate vertical basis. SDK Vector3
  uses Y as altitude (X/Z are horizontal). `Playfield.Raycast` uses outdoor
  TilemapSurface; these probes are hints about client-visible geometry.
- Flight now always retains a finite estimate for a finite local entrance,
  attempts direct movement toward an elevated point 4 m outside the entrance,
  and prepares descent only within 16 horizontal metres. There is no synthetic
  climb/cruise/descent feasibility gate. Local terrain/live door position sets
  approach height; a newly visible door can upgrade descent once. Without a
  height hint or usable accepted height, a short provisional descent leads to
  the user's landing/dismount step, not rejection or blind descent to map Y=0.
- Ground mesh costs still require a complete path; other candidates use a
  horizontal direct estimate. Fallback samples 16 headings at 2/4/8/12 m,
  including tangent/backtracking arcs, scoring distance improvement, terrain,
  clearance, recent visited/failed points and detour-side continuity. Probes
  are soft scores; even all-hit sampling permits a short exploratory leg.
  Execution resamples from the actual position after arrival/stall, not after
  a fixed number of obstruction probes. Recent point memory is bounded to 24.
- Eight seconds without waypoint improvement causes ground/flight recovery;
  stationary mesh movement switches to direct after 15 seconds. Flight samples
  lateral/raised recovery legs with a ceiling initial cruise height +40 m.
  Recovery does not reset final-target progress. New best horizontal ground
  distance/new best 3D flight distance must improve within 90 seconds; useful
  movement can continue up to the 15-minute travel limit. Dismount waits two
  minutes. Precise approach/entry has a three-minute overall limit, unresolved
  unique door lookup 45 seconds, and door use remains three attempts/20 seconds.
- Both modes converge on the same live-door approach. Door lookup uses the
  accepted X/Z neighborhood (6 m); accepted Y alone cannot exclude it. Unique
  identity is retained across entry, and actual live door distance/height plus
  grounded/nonfalling state still govern interaction. Exact dungeon/quest
  verification is unchanged. These local samples do not guarantee global paths.
- Logs separate candidate estimates, submitted active waypoint/final target,
  five-second progress reports, eight-second advisory collision hints,
  obstacle recoveries, descent/height evidence, interaction and HARD FAILURE.
- Changed only local planner/travel, coordinator's outdoor wait wording, and
  README/PROJECT_MEMORY/CONVERSATION_LOG. Accepted-mission tracking, dungeon
  exploration/combat/room doors/lockpick/loot, assets, dependencies and exact
  dungeon handoff are unchanged. Source/API inspection only: no compilation,
  package restore, local tests or in-game run. User pulls main, compiles and
  validates ground recovery/flight approach/descent/door interaction in game.

## Nearest Entrance from Captured Origin and Vehicle Entry (2026-09-28)

This correction supersedes all earlier route-cost ranking and dismount steps.

- User's 07:09-07:10 logs confirm successful FlightLanding/descent, then a
  dismount prompt and ground obstacle arcs for the last few metres. The user
  requested estimating the nearest entrance from the origin and building only
  its path, plus staying in the vehicle when a flying path was chosen.
- `SelectNearest` captures player position once, compares horizontal entrance
  distances only (mission ID breaks ties), and calls `LocalRoutePlanner.Plan`
  exactly once for the chosen mission and active mode. Other missions receive
  no terrain, navmesh or flight planning. The route stores origin and accepted
  entrance coordinates separately from the mutable accepted-mission record.
  Path cost is informational for the chosen route, not a ranking criterion.
- Removed automatic mission reselection on movement-state changes. The chosen
  mission/entrance persists through local travel and recovery. Explicit mode
  change/stop-start, mission removal, zoning, or completion can select anew.
  Same-playfield continuation chooses nearest from the next captured origin.
- Removed Dismount phase and two-minute wait. FlightCruise -> FlightDescent ->
  EntranceApproach -> EnterDoor retains flight steering and the vehicle. Final
  approach gets the 100 ms flight update cadence; live door use permits Fly
  for a selected flight route, retaining actual distance/height and identity
  checks. If the user manually leaves flight near the entrance, the same
  mission can continue on ground; the bot never requires that action.
- Descent/approach side uses the captured origin. Local terrain/live door height
  still resolves stale accepted altitude. Cached resolved entrance height keeps
  a missing live door from causing a moving flight target. Without a unique
  exposed door, move to the chosen entrance trigger point (within 0.7 m) and
  await visibility or zoning; do not stop two metres short. Ambiguous live
  door use remains withheld; exact dungeon/quest verification is unchanged.
- Nearest-selection logs include origin, chosen mission/coordinate, estimated
  distance and single route/mode/cost. Flight descent, final movement and door
  use explicitly report staying in vehicle. Existing advisory probes, bounded
  progress/stall recovery, 15-minute travel and three-minute approach/entry
  limits remain; unresolved door/proximity entry waits 45 seconds.
- Source/diff inspection only. No compilation, restore, automated local tests
  or in-game run. User pulls main, compiles and validates in game. Dungeon
  exploration, combat, interior doors/lockpick/loot and accepted tracking are
  unchanged. Updated README and conversation history along with this memory.

## Entrance Height Refinement and Proactive Flight Avoidance (2026-09-28)

This section supersedes single-hit entrance height and stall-only flight avoidance.

- User's 07:27 logs on main `c01a005` show flight recovery and vehicle entrance
  approach near (699.33, 28.45, 1535.32). They report pathing otherwise fine,
  requesting better entrance elevation and avoidance of buildings/trees by
  climbing or going around them. Keep nearest-from-origin selection and vehicle
  entry; no changes to accepted tracking or the working dungeon systems.
- Previously, one downward hit could be a roof/canopy, and the surface four
  metres outside could replace the actual entrance elevation. Height stayed
  cached until a door appeared. Collision hints waited for an eight-second stall.
- The chosen entrance now uses 17 columns (centre, 2 m and 6 m rings), up to
  four downward surface layers, and walkable normals. At least three independent
  columns must support a height within 1.5 m, with centre/inner support. Lower
  layers win equally supported ties; centre height or the inner-ring mean gives
  local entrance elevation. Only the selected mission is sampled. A unique live
  door is authoritative and cannot subsequently be overwritten by terrain.
- Local descent and entrance approach refresh surface evidence every two seconds
  while within 24 horizontal metres, with 0.5 m height/position tolerance. Keep
  staging terrain separate from entrance floor; short rays near resolved height
  avoid reintroducing a high roof into staging. Zero/stale accepted height remains
  provisional and cannot veto the route. Flight still descends/enters in vehicle.
- Inspected cached AOSharp reference and pinned AOSharpSDK 1.0.106 assembly
  metadata without loading/executing the SDK. Confirmed `Playfield.LineOfSight`,
  `Playfield.Raycast`, and float player `Radius`/`Velocity` APIs. Raycast uses the
  outdoor TilemapSurface; native line-of-sight supplements scene-object checks.
  These queries cover client-loaded geometry and do not prove global feasibility.
- Every 400 ms, flight checks an 8-24 m speed-based lookahead corridor with five
  centre/lateral/above surface/line-of-sight rays. Clearance offsets exclude the
  space below player origin, so near-floor entry does not falsely block every climb.
  Detected obstruction or an eight-second
  waypoint stall selects among clear vertical climbs (4/8/16/24 m) and eight-heading
  lateral/raised legs (6/12 m long, 0/6/12 m rise). Continuation clearance and recent
  points affect score. Ceiling is 40 m above max(initial cruise, refined floor+12).
  No bypass found means stop and retry locally while keeping the chosen mission;
  the original final-target no-progress clock still bounds the attempt. Exempt only
  the last metre at the selected portal for proximity entry, not the whole approach.
- Height corrections adjust flight distance baseline without resetting the progress
  deadline. Existing 90-second final-target no-progress, 15-minute travel,
  three-minute approach/entry, 45-second unresolved-door, and three uses/20-second
  interaction bounds remain. Logs identify floor/source changes, proactive avoidance,
  obstacle bypass target, stalled recovery, waiting for a clear bypass and hard failure.
- Changed only local planner/travel and README/PROJECT_MEMORY/CONVERSATION_LOG.
  Ground waypoint recovery, nearest mission selection, accepted tracking, verified
  handoff and dungeon exploration/combat/interior doors/lockpick/loot are preserved.
  Source/API/diff inspection only: no compilation, restore, local tests or in-game
  run. User pulls main, compiles and validates height and obstacle avoidance in game.

## Lower-Entrance Descent and Flight Smoothing (2026-09-28)

This correction extends proactive avoidance with lowering paths and waypoint commitment.

- User's 07:58 logs after `13ee0dc` show FlightDescent at Y=39.87 with a final
  target Y=23.5, 19.7 m remaining and no improvement for 20 seconds. Bypasses
  36-38 stayed at the same altitude. Avoidance seemed useful, but the character
  struggled above a lower entrance and path changes looked jerky.
- Source diagnosis: generic bypass candidates could only remain level or climb;
  they could not leave a blocked diagonal descent via a clear lower column.
  Every probe hit could replace the waypoint immediately. Arrival stopped movement,
  minor goal changes discarded detours, and direct LookRotation snapped direction.
  A reported hold also failed to persist between 400 ms probe updates, allowing
  forward movement to resume on the next tick toward the blocked goal.
- Add a dedicated descent-column search for obstructed/stalled local approaches
  at least 2 m above the target. Sample current/approach columns and eight-heading
  rings at 4/8/12/20/28 m around the approach. Validate horizontal alignment at
  current altitude and the full vertical drop. Prefer low-level continuation to
  the final approach and smaller turns; nearby terrain can raise the lower endpoint
  for 1.5 m hover clearance. Keep the two-leg plan, then recheck descent from the
  actual alignment position before lowering. General bypasses also sample 3/6/12 m
  downward arcs, bounded by target altitude, alongside lateral/raised candidates.
- Keep a clear active waypoint until arrival, confirmed obstruction or eight-second
  stall. Two consecutive blocked probes confirm replanning; movement stops on the
  first suspect hit and remains held between probe updates. No useful candidate
  means persistent hold/retry, with the final-target deadline continuing. Height
  refinements keep the bypass; a >1 m correction invalidates cached descent data.
- Arrival radius is 1.2-2.5 m based on speed, avoiding small-target overshoot;
  cached descent is always rechecked from the actual position. Normal waypoint
  transitions no longer unconditionally halt. Flight steering limits direction
  change to 120 degrees/second, rotating in place for >20-degree turns and checking
  the actual heading for smaller moving turns. The final entrance exemption remains
  limited to the last metre of an aligned approach; exact door/proximity checks
  and vehicle entry are preserved. Direction scoring discourages sharp reversal.
- Inspected pinned movement queue/update behavior and cached Quaternion/Vector3
  implementation. Confirmed Quaternion.Forward, Vector3.Normalize/Dot and
  Quaternion.LookRotation in AOSharpSDK 1.0.106 Common assembly metadata without
  executing the SDK. Smoothing handles vertical and opposite directions.
- Logs add descent-column alignment/lowering target and vertical gap in flight
  progress. Existing 90-second final-target progress, 15-minute travel and bounded
  approach/door-use limits remain; replans never refresh the final progress clock.
- Changed only local planner/travel and README/PROJECT_MEMORY/CONVERSATION_LOG.
  Nearest-from-origin selection, accepted tracking, floor refinement, ground fallback,
  vehicle entry and dungeon exploration/combat/interior doors/lockpick/loot remain.
  Source/API/diff inspection only; no compilation, package restore, local tests or
  in-game run. User pulls main, compiles and validates descent and smoothing in game.

## Committed Flight Paths and Height Before Entry (2026-09-28)

This correction supersedes short flight bypass sampling and descent at 16 m.

- User reports worse pathing than the previous version: characters move only a
  couple of units instead of following a coherent path. Reposted 07:58 lateral
  avoidance 37/38 logs show the repeated short targets. Requested selecting the
  entrance height when within 1-2 units, then proceeding to enter the mission.
- Inspected current main `141cf24`. Flight still committed only one short bypass
  or a two-leg column, reacted to scene/offset probe hits, and stopped to rotate
  for turns over 20 degrees. There was no complete waypoint sequence from current
  position to final approach. Replaced that execution/planning model.
- `PlanFlightPath` compares complete paths for the selected destination: direct;
  climb/lateral/cruise/lower/approach using 8/16/28 m lateral offsets and 0/12/24/40 m
  raised levels within the fixed ceiling; and outward/down/return sequences for
  lower targets. Score every leg using surface/native LOS clearance probes and
  recent points. Known centre surface hits carry much more weight than uncertain
  scene LOS, so all-inconclusive LOS cannot force a known blocked direct descent
  over a ray-clear detour. Prefer clear paths; retain a finite estimated attempt
  if none clears. Mission selection still estimates nearest from one captured
  origin, plans only that mission and preserves its identity/accepted coordinates.
- `LocalMissionTravel` retains and follows the full waypoint list. Advance normal
  legs continuously, using 1.2-2.5 m intermediate arrival based on speed. Check the
  next segment from the actual position before cutting a corner. Replan
  only for a changed goal, eight-second observed waypoint stall, or a nearby
  surface obstruction sustained for one second, with a three-second retry interval.
  Short actual surface checks can stop a physical obstruction; speculative scene
  LOS/offset hints cannot repeatedly stop movement. Holds persist between updates.
  Removed the rotate-in-place gate. Smooth moderate heading changes; face sharp
  or corner-conflicting legs directly to retain continuous motion.
- Flight phase sequence is now FlightCruise -> EntranceHeight -> EntranceApproach
  -> EnterDoor. Travel to about 1.5 m outside the selected entrance. At <=2 m
  horizontal distance, refresh/select actual entrance floor from a unique live
  door or the existing 17-column/layer consensus. Align to floor+1 m vehicle
  clearance, with <=0.75 m vertical error and <=2 m horizontal distance, then
  permit entry. Recheck height before door use; a live-height change or drift
  returns to alignment. Blocked height alignment plans a complete detour down
  outside a roof and back to the same target. No automatic dismount.
- Height remains provisional if geometry/live door data is unavailable; accepted
  zero/stale height alone is not a route veto. Live door height cannot be overwritten
  by terrain. Entry uses a <=2 m horizontal approach, actual 3D/height checks and
  unique identity. Proximity entry and exact selected-dungeon verification remain.
  Ground fallback and all dungeon exploration/combat/interior door/lockpick/loot
  code are preserved. Three-minute approach/height/entry, 90-second flight final
  progress, 15-minute travel, 45-second unresolved door and bounded use remain.
  Complete path replans and height corrections cannot refresh final progress.
- Logs list the whole committed path, leg index/count, replan reason, selected
  floor/source within 2 m, aligned entry height, progress and hard failure.
  Updated README/CONVERSATION_LOG. Source/API/diff inspection only; no compilation,
  package restore, local tests or in-game run. User pulls main, compiles and tests.

## Connected Detours Around the Entrance Building (2026-09-28)

This replaces the fixed flight-path templates above while preserving committed
movement and the within-2-metre height-before-entry sequence.

- User's 08:53 logs show improved movement but repeated plans 5/6 of the same
  blocked one-leg estimate at EntranceHeight. Character is about 6 m from the
  target with only 0.3 m vertical gap; the mission building blocks the path.
  User requests a way around the building/tree/cave.
- Inspected main 20dac70. Fixed rectangles cannot follow connected corners around
  a building; lower-target ring paths were not sampled at nearly equal height.
  Scoring all paths with hits allowed the blocked direct leg to win repeatedly.
  Recent target points did not record which movement direction actually failed.
- Flight planning now uses a bounded A* search linking eight horizontal neighbours
  on a 4 m grid and vertical columns at current, target, raised +12/+24 m and
  the fixed ceiling. The ceiling remains max(initial cruise, refined floor+12)+40.
  Search includes same-height routes, multiple building faces, outside descent
  columns and paths toward an opening. It expands at most 450 cells and performs
  at most 6,000 surface probes per attempt; margins grow 32/48/64/80 m on retries.
  Centre surface hits exclude edges, while body offsets rank clearance. Missing
  geometry/ambiguous native LOS does not invalidate a finite mission estimate.
- Connect cells to the exact final target, or during EntranceHeight to one of
  16 points at 1.5 m around this same entrance at floor+1 m. This can replace a
  blocked original-side anchor with a reachable side while preserving selected
  floor, accepted mission coordinates and unique live door identity. No new
  mission is selected and no dismount is required. Height alignment also requires
  a clear segment to the chosen anchor, so passing within 2 m behind its wall
  cannot prematurely discard the committed detour.
- Reconstruct and simplify the complete route using clear body-aware shortcuts;
  follow the existing waypoint sequence and moving-turn behavior. Grid cells are
  planning samples, not separately replanned micro-hops. Observed eight-second
  stalls/sustained obstruction record up to 16 short failed directions, preventing
  reuse even when a scene object is absent from surface ray data. This observation
  memory survives phase changes and clears on reset/new mission.
- A known blocked centre/failed leg can no longer return as a one-leg estimate.
  If no connected route is found within bounds, halt and retry after three seconds
  with an expanded margin. Ray-clear body-hint estimates remain attemptable.
  Replans, holds and changing entrance side retain the 90-second final progress
  clock. Existing 15-minute travel, three-minute entry and bounded door use remain.
- Rechecked AOSharp reference Playfield.Raycast (outdoor tilemap/dungeon surface;
  missing surface returns no hit), native LineOfSight (false may be unavailable),
  and Vector3 division/normalization APIs. No new dependency or SDK API is needed.
- Logs add connected route/cell/blocked-edge/margin information, observed blocked
  legs, waiting for search retry and changes to the entrance alignment side.
  Changed only local flight planner/execution and README/CONVERSATION_LOG/memory.
  Ground fallback, accepted tracking, nearest-from-origin selection and dungeon
  exploration/combat/interior doors/lockpick/loot are preserved. Source/API/diff
  inspection only; no local compilation, restore, tests or in-game run. User pulls
  main, compiles and checks this obstructed building approach in game.

## Flight Launch After Bounded Search Exhaustion (2026-09-28)

This corrects the stationary launch regression in 29803a5 without removing the
connected obstacle search or the near-entrance height sequence.

- User's 09:30-09:31 logs: Fly state, no outdoor mesh, mission 1442233633 selected
  from (649.0104,5.515,1314.629), accepted entrance (626.6214,0,1461.9), 149 m away.
  Height is provisional player Y; cruise target Y=17.515. Repeated empty searches
  report exactly 183 cells/103 blocked edges while margins grow 32/48/64/80 m.
  Character never moves; no observed failed legs exist.
- Source diagnosis: only cells within 12 m could connect to the target. The
  fixed 6,000-probe cap could be reached before local-grid expansion arrived there.
  All reachable sections were discarded unless a complete path existed. Budget
  exhaustion returned the same negative value as a physical hit, inflating the
  obstruction count. Widening the margin did not increase useful search work.
  Grid search also consumed the shortcut budget. Cruise never refreshed a distant
  zero-height entrance until height alignment, potentially aiming below local terrain.
- Restore cheap whole direct/climb/cruise/approach candidates at current/target,
  +12/+24/+40 raised levels within the fixed ceiling. Prefer body-clear candidates.
  A long validated climb/cruise section can launch when its final descent/approach
  is still unresolved. Connected search remains for buildings/trees/caves and can
  connect cells to the target at any distance. Search stops around 4,000 probes,
  reserving the rest of the 6,000 total for combining clear stretches.
- If complete access remains unknown, retain the best useful validated prefix
  from candidate legs or finite-cost grid nodes. Require >=8 m horizontal or
  >=6 m vertical displacement and improved approach score, or a validated launch
  climb >24 m from the entrance to leave a low/canopy start. A launch climb may
  temporarily increase final distance under the same clock. Simplify/commit the
  whole prefix. Hold only if neither complete route nor useful prefix exists.
  A known blocked direct leg is still excluded, and observed failed-leg memory stays.
- Execution distinguishes complete routes and prefixes. Actual arrival on the
  final prefix leg with local clearance immediately continues planning from the
  new position. This is neither an obstruction nor a final entrance: it does not
  record a failed direction, move the entrance anchor, enable the final doorway
  probe exemption or reset the 90-second final progress clock. Minor destination
  changes cannot overwrite a validated prefix endpoint with the unresolved goal.
- During cruise within 24 m, refresh the floor estimate from live door/17-column
  consensus and raise cruise clearance to at least floor+12. Final floor selection,
  alignment and vehicle entry still follow the within-2-metre sequence. Accepted
  zero/stale coordinates do not require an outdoor mesh or a dismount.
- Logs show complete=True/False, path/prefix leg, actual prefix arrival continuation,
  probe/cell budgets and physical/observed edge counts. Budget exhaustion is now
  distinct from obstruction. Nearest-from-origin selection, accepted mission/door
  identity, ground fallback and dungeon exploration/combat/interior doors/loot stay
  unchanged. Updated README/CONVERSATION_LOG. Source/diff inspection only; no local
  compilation, restore, tests or in-game run. User pulls main, compiles and checks
  launch from the reported position and subsequent entrance avoidance in game.

## Outside Descent After Cruise Arrival (2026-09-28)

- User's 09:54 logs from Broken Shores: mission 1442233602 selects a 103.8 m
  flight, follows two committed legs, and reaches within 2 m horizontally. The
  local floor refines from provisional 28.575 to 15.2 (17/17 columns). Selected
  entry height is 16.2; character is at Y=40.625. EntranceHeight then repeatedly
  returns no useful section: 79 cells, 4,010 probes and 1,405 blocked edges, with
  margins growing 48/64/80 m. Cruise works; final descent still stalls in the air.
- Inspected main 7c591ea. All 17 low entrance targets are tried directly from
  each expanded high cell, consuming the budget before reaching an outside
  column. Grid heights contain only current, final and raised planes. The prefix
  score weights vertical gap by 0.2 while counting all horizontal retreat, so
  even a useful descent beside the building can be discarded. Only launch climbs
  have an exception for temporary retreat; entrance descent does not.
- Add a dedicated outside-column search before generic flight search whenever
  current altitude exceeds final height by more than 3 m and the direct segment
  needs avoidance. Sample 16 directions on 4/8/12/20/28/40/56/72 m rings inside
  the current margin. Certify current-height sideways movement and vertical drop
  as a connected section; prefer a complete return to this same entrance when
  possible. Its budget is about 2,000 probes within the existing shared limits.
- Test selected entry height and intermediate 6/12/18 m lower planes. At final
  height, test the doorway/clear-side return; intermediate planes are prefixes,
  avoiding repeated doorway rays from every high staging point. Preserve a
  useful verified drop of >=6 m, or the entire requested drop when 3-6 m remains,
  even if the final doorway leg is still blocked. Actual prefix arrival continues
  planning from the lowered position using the existing executor, without marking
  success or changing the floor/entrance anchor. Keep the entire path and certify
  shortcuts so a diagonal cannot cut back through the roof.
- Generic fallback adds intermediate lower grid planes and scores prefixes by
  actual 3D distance plus route cost. A necessary >=6 m lower section may temporarily
  retreat horizontally. Full-path/failed-leg geometry rules, 90-second observed
  final-target progress and three-minute entry limits remain; search/replans cannot
  reset those clocks. Vehicle equipment/movement state is unchanged.
- Logs identify outside alignment versus lowering beside the entrance and record
  tested columns, clear drops, the selected lowering point and direct-drop surface
  Y. Rechecked the existing AOSharp Raycast hit-position API; no new SDK API or
  dependency. Terrain at a staging column never overwrites the selected doorway
  height. Unique door identity, nearest mission, ground fallback and all dungeon
  exploration/combat/interior door/lockpick/loot behavior remain untouched.
- Updated README/CONVERSATION_LOG and inspected source/diff only. No local build,
  package restore, tests or in-game run. User pulls main, compiles and validates
  outside descent and return to the reported Broken Shores entrance in game.

## Observed Descent Footprint and Precise Outside Alignment (2026-09-28)

- User's 10:27 logs: all previous mission paths were okay. This entrance lowers
  to Y=19.901 but cannot reach selected entry Y=16.2 (terrain floor 15.2).
  After recording the failed vertical leg at (750.8332, 19.901, 1458.063),
  the planner chooses another drop at (749.2946, 19.901, 1458.335), only about
  1.6 m away. Both stop at the same altitude despite reported clear drops.
- Inspected main 2172f9f. Failed-leg memory covers only a small directional
  witness about 2 m along the failed segment. Neighbouring vertical columns
  remain eligible. Execution checks only surface rays when advancing/steering,
  and its speed-based arrival radius can start a descent up to 2.5 m short of
  the selected outside column or begin the return before fully lowering.
- Rechecked AOSharp reference Playfield.Raycast: outdoor queries use
  TilemapSurface.GetLineIntersection. A clear surface ray is not evidence that
  actual vehicle movement can cross scene-object geometry. Reuse existing
  position/movement APIs; no new SDK dependency or native collision API.
- Only an eight-second stalled, actively commanded, nearly vertical descent
  during EntranceHeight within 24 m of the selected entrance learns a broader
  footprint. A paused geometry probe cannot create/expand one. Initial radius
  is 4 m; another observed stall inside it within 1 m of the same stopping
  altitude expands by 4 m, bounded at 16 m. Memory retains the existing 16-entry
  cap and clears on reset/selection. Other failed movement keeps its short-leg
  memory. Successful cruise and the route selection/search algorithm are unchanged.
- The learned geometry is a crossing plane 0.5 m below the stopped position,
  with vehicle radius added to its horizontal footprint. Avoid crossing it from
  either side inside the area; allow sideways escape at the stopping altitude
  and travel below it after an outside descent. Do not block the entire column
  down to the estimated entrance floor or infer a new door height from the stall.
- The existing planner/shortcut checks use that memory. Execution now checks
  it too on prefix arrival, corner transitions, nearby probes and smoothed turns.
  EntranceHeight staging arrival tightens to 0.75 m before a following descent;
  a lowering leg must finish within 0.75 m vertically before advancing to the
  return or completing a prefix. Cruise waypoint tolerance remains unchanged.
- Logs distinguish an observed descent area, held/requested height, radius
  growth and retained selected entry height; committed paths include the number
  of remembered descent areas. Live door height remains authoritative, and
  within-2-metre height alignment, vehicle entry, unique door identity, exact
  dungeon handoff, 90-second progress and three-minute entry bounds remain.
  Ground fallback and dungeon exploration/combat/interior doors/lockpick/loot
  code are untouched. Updated README/CONVERSATION_LOG. Source/API/diff inspection
  only; no local compilation, restore, tests or in-game run. User pulls main,
  compiles and validates a farther outside drop/return at this entrance in game.

## Early Clear Flight Approach and Waypoint Braking (2026-09-28)

- User says the previous solution is clearly not working. The 10:45 logs follow
  a two-leg route from (751.9482, 17.2718, 1468.75) to final alignment target
  (751.2989, 16.2, 1463.457), repeatedly alternating "lowering beside entrance"
  and "entrance height alignment" for that unchanged target. User stopped the
  bot and requested appropriate height selection before reaching entrances
  while pathing, when no objects obstruct the route. This supersedes the earlier
  requirement to delay height selection until within 1-2 m; keep final alignment
  and precise entry checks there.
- Inspected main bf05957. Cruise refreshes height within 24 m but preserves
  elevated cruise altitude until within 2 m. FlyMove commands continuous forward
  movement to within only 0.1 m of its target, with 100 ms updates and no arrival
  braking. Labels depend on current vertical gap crossing 1 m, explaining the
  repeated text without a new target or phase. Logs alone do not establish the
  exact physical entrance height or prove a particular object at that location.
- Within 24 m, once live door/local surface data has resolved entrance height,
  check a direct approach every 500 ms using centre/side/upper body rays and
  observed obstruction memory. A clear corridor begins EntranceHeight early,
  committing travel to the chosen outside point at approach altitude. If the
  corridor is blocked/inconclusive, retain elevated cruise/obstacle planning;
  do not reject the mission. Early alignment remains in the same phase until
  within 2 m horizontally and 0.75 m vertically; subsequent obstacles retain
  bounded recovery. Provisional distant zero height still cannot start a blind
  early descent. Nearest-from-captured-origin selection is unchanged.
- Replace fixed floor+1 targets with consistent vehicle clearance, radius+0.25
  bounded to 1.5-2 m. This keeps approach altitude above the surface estimate
  without treating a roof stop as the door height. Planner goal/ring, alignment,
  proximity entry, live-door movement/drift checks and logs share the clearance.
  Keep live door use's existing <=2.5 m height and <=3/3.5 m distance gates;
  floor/live door identity and exact dungeon handoff are unchanged.
- For EntranceHeight/EntranceApproach waypoints within 6 m, request 25 ms game
  updates, steer precisely to the committed target and release forward if
  measured velocity over the reaction interval predicts reaching/passing the
  0.45 m arrival tolerance. Resume after slowing if still outside tolerance.
  Cruise retains 100 ms and other phases 250 ms. The coordinator's single
  timing line reads this interval; dungeon/ground logic is unchanged. No game
  speed, position or movement-state writes are added.
- Save the actual planning origin and classify each committed leg's purpose
  from that origin/previous waypoint, not the character's changing altitude.
  Logs stay stable for the same leg; report early selection/deferred approach,
  radius/clearance, and velocity alongside vertical gap. No log-only suppression
  hides changes to the actual target. Braking, changes of estimate and replans
  cannot reset the existing progress/entry deadlines; entering height alignment
  early starts its existing three-minute bound earlier.
- Initial height anchors now use a 1.5 m outside point on the current side (the
  captured-origin side for early approach), rather than a vertical target at an
  arbitrary current distance up to 2 m. If the live entrance shifts, reproject
  an anchor outside 1.51 m while retaining its side. Together with 0.45 m arrival,
  this leaves margin inside the final 2 m gate instead of stopping just outside it.
- Rechecked AOSharp Dynel.Radius/Velocity/Position/Rotation and pinned
  SMovementController: movement actions are queued and drained on game updates;
  Halt stops navigation and the existing FullStop action releases movement.
  Reuse these APIs; no SDK dependency or native interop additions. Retain
  obstruction memory/search, vehicle equipment, ground fallback and working
  dungeon exploration/combat/interior doors/lockpick/loot. Updated README and
  CONVERSATION_LOG. Source/API/diff review only; no build, restore, local tests
  or in-game run. User pulls main, compiles and checks early clear descent and
  stable final approach in game.
