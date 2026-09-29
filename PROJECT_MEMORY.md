# RKMission Project Memory

_Last updated: 2026-09-29_

This file is a durable project-context summary for future RKMission development sessions.

## Project

- **Name:** RKMission
- **Repository:** `Phobius-cpu/RKmission`
- **Game:** Anarchy Online
- **Automation framework:** AO# / AOSharp
- **Primary target:** Rubi-Ka missions

## Intended Bot Workflow

This current responsibility boundary supersedes the historical rolling/travel
implementations below; keep that chronology as evidence of earlier outcomes.

1. The user rolls/selects/accepts any number of missions and handles every
   inter-playfield transfer, vehicle equipment and return-item terminal hand-in.
2. `/rkm start` arms takeover only for accepted Rubi-Ka missions in the current
   outdoor playfield. Rank active-mode estimates from a captured current origin:
   complete optional Run mesh cost or horizontal fallback; horizontal Fly estimate.
   Upload the exact live accepted mission via native `Mission.UploadToMap`.
3. One `LocalMissionTravel` state machine owns coarse travel, radial exterior
   probing/Ground OrbitBypass, reactive Fly over/around comparison, elevation alignment,
   final normal approach, interaction/crossing and
   transition wait. Shared `LocalMovement` handles observed Run/Fly execution.
   Outdoor mesh and synthetic flight clearance never gate finite direct travel.
4. Quest coordinates are anchors, including nonzero/stale Y. Within 24 m by
   default, search 16 angular sectors at 12/20/6 m movement rings. Associate live
   Doors only inside a fixed 6 m anchor radius with the existing quest/context/
   ambiguity checks; never promote shops or remote buildings. Wider movement
   rings exist to walk/fly around geometry, not enlarge door ownership claims.
5. Rotation-derived normals are the strongest live signal, with both exterior
   signs validated. Without a Door, infer sides from radial observed progress.
   Same/neighbor-bearing stalls at similar position/radius trigger perimeter
   recovery, not merely opposite-sector ranking. `EntranceOrbit` replans short
   tangential legs from actual position, widens radial clearance, compares measured
   CW/CCW progress, preserves the other direction and verifies actual side change.
   Fly uses one `FlightPathPlanner` from transit through side relocation, compares
   over/around immediately, retains actual cruise height through obstacles, and
   resolves entry height only after reaching the exterior using live/verified/local
   support. The broad multi-layer floor voter remains Ground-only.
   Fixed target minima/angular coverage survive retries. Only reached exteriors
   count as coverage; exhausted bypass may stop with an unresolved-route reason
   after no progress. Defaults: 8 s leg stalls, 90 s no-progress, 15 min total.
6. Persist runtime settings/diagnostics under deployed `RKMissionData`. Stable
   playfield + 2 m quantized X/Z anchor keys contain optional mission IDs per
   attempt. Keep 256 entrances/96 records each by default, including failures,
   interruptions and final transition results. Learn a successful exterior vector
   only after exact verified mission dungeon entry; also learn bypass direction,
   ring radius, traveled angular span and successful sector. Keep requested sector
   separate from wall-bearing sector and retain failures. Revalidate on later visits.
7. The existing stable exact mission/dungeon/room check gates `MissionDungeon`.
   Dungeon entry/post-combat readiness now gates new room actions and enemy
   acquisition starts within 20 m. Preserve exploration, room/door crossing,
   lockpick and ordinary loot rules behind that gate. Cleared-room enemy checks
   require membership in that specific room; confirmed reverse edges wait only
   one second. Objective ordering now
   reserves known objective rooms when possible and always defers completion
   until every other enemy is dead and ordinary rooms/loot are cleared. The outdoor
   coordinator retains managed navigation evidence through zoning and records
   the existing exact verification.
8. Non-return objectives require our final action, observed objective/server
   evidence and exact quest absence for two seconds, with no manual deletion.
   Then finish remaining objective NPC/corpse/loot, record automatic completion,
   route back to verified external-door geometry and wait for actual outdoor
   zoning before selecting the next closest local mission. For return-item
   missions, collection after clearance completes the bot run with a separate
   manual-hand-in-pending flag; no terminal navigation/use or reward claim.
   `/rkm complete [bound id]` remains a manual override. Stop when none remains
   and leave inter-playfield transport to the user.

Settings reload on plugin load; existing `/rkm travel auto|ground|flying` commands
remain. No new chat commands; automatic objective finale and dungeon exit are
now authorized. Missing routes, unfinished/skipped loot or unclear objective
evidence must not be silently treated as successful automatic completion.
Do not compile, restore packages or test locally; the user owns in-game validation.

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

## Fixed Entrance Coordinates, Height, Then Entry (2026-09-28)

- User's 11:08 logs: EntranceHeight is 9.3/6.4/4.9 m from target
  (554.4971, 16.5, 1473.821), with vertical gaps 0.7/3.8/2.6 m.
  Search repeatedly exhausts about 4,040 probes after 87-89 cells, with
  1,640-1,698 physical/observed blocked edges and zero observed failed legs.
  User requests the coordinates and height be set 1-2 m from the entrance
  before entering, for all failures of this type, and asks whether it resolves
  the issue. Source changes can be checked here; game behavior requires their
  compilation/in-game validation under the existing no-local-test instruction.
- Inspected main 6d41771. Direct final altitude planning still requires a
  complete clear section or a relatively long validated prefix. The planner
  tests 17 low doorway goals from each cell, while final small descents <=3 m
  miss the outside-column search. A zero-length result prevents movement even
  with no observed failure. Alignment also requires a clear synthetic ray after
  position/height have already been physically reached.
- Add EntrancePosition for all flying routes within 24 m, including unresolved
  height. Commit one 1.5 m approach point and a selected height. If locally resolved
  height/direct body corridor is clear, permit early height adjustment during
  coordinate travel; otherwise hold at least current altitude/desired clearance
  while travelling to that neighborhood. Reach actual horizontal distance 1-2 m,
  then enter EntranceHeight and confirm the same distance plus vertical error
  <=0.75 m before EntranceApproach. No surface-ray certificate can veto an already
  aligned physical position. Keep the vehicle throughout; no direct position,
  altitude, speed or movement-state writes are introduced.
- Keep the chosen approach point across retries and subsequent drift recovery.
  Only live door coordinate changes reproject its 1.5 m offset; local floor/live
  door height changes update its height. FlightEntryPoint/vehicle clearance stays
  consistent with 6d41771. An outside staging column or validated prefix cannot
  overwrite this anchor. Re-enter coordinate/height stages on loss of alignment;
  the three-minute entry deadline now starts at EntrancePosition and never resets.
- Without a live door, proximity entry uses <=0.45 m horizontal and <=0.75 m
  vertical error. The old 0.7 m 3D check could require further descent despite
  a height offset of 0.71-0.75 m already accepted by alignment; keep those
  tolerances consistent while waiting for actual zoning/door visibility.
- Remove the 16 alternate doorway goals and ref destination mutation. The
  connected/descent planner receives one stage target and an explicit entrance
  stage flag, retaining cruise launch semantics. Final descent search runs above
  a 1 m gap; cruise retains its old 3 m threshold. Regular search still certifies
  centre segments and preserves useful prefixes/observed blocked-leg geometry.
- If it returns empty in a committed final stage within 24 m, rank direct and
  whole outside/lower/return estimates at 4/8/12/20 m, 16 headings, and current/
  target level or +4 m. Bound the extra centre-ray work to 512 probes and use
  hits as scoring hints. Reject all candidates that repeat known failed movement.
  With zero observed failures, a finite nontrivial direct target remains attemptable
  instead of indefinitely awaiting a complete geometry certificate. Do not simplify
  these unverified sections through obstacles; execute the committed sequence.
- Advisory paths retain observed-failure checks on steering, probes and waypoint
  transitions, while synthetic surface hits are logged soft hints. Actual eight-
  second stalls record failure and trigger normal search/another coherent estimate.
  Probe-only pauses cannot populate failed-leg memory; record only actively
  commanded stalled movement that was not being held by the surface probe.
  Retain descent footprint memory, near-waypoint braking, 25/100/250 ms cadence,
  90-second progress, 15-minute travel and three-minute entry bounds. If every
  estimate repeats known failures or the clock expires, hold/fail normally.
- Logs show fixed approach point, EntrancePosition, coordinates reached, actual
  radial distance, coordinates/height aligned, advisory=True and committed attempt
  legs. Avoid the empty descent-detail double semicolon. Same mission/door identity,
  unique door/use range checks and exact dungeon verification remain. Ground
  travel/fallback and dungeon exploration/combat/interior doors/lockpick/loot are
  untouched; only the two local-travel files change. Updated README and
  CONVERSATION_LOG. Source/diff/API inspection only, no local compilation, package
  restore, tests or game run. User pulls main, compiles and checks the full staged
  sequence plus actual movement on the reported zero-failure searches in game.

## Entrance Coordinate Sources and Linear Approach (2026-09-28)

- User identifies the troublesome doorway as X=553.2, Z=1475.0, height Y=18.1,
  playfield 665, and requests corrected coordinates, 1-2 m approach radius,
  object checks/detours before entry, and navigation as linear as possible.
- Inspected main bb47f40 and supplied AOSharp reference sources. `Mission.Location`
  obtains `GetQuestWorldPos` local `Pos`; `AcceptedMissions` stores it without axis
  swapping. `Vector3` is X/Y/Z, while the user's AO coordinates are X/Z/Y. Existing
  movement logs called an offset waypoint the entrance/final target, obscuring
  that distinction. No tracker or SDK coordinate conversion change is needed.
- Actual source inconsistencies: `ResolveEntranceHeight` could replace a nonzero
  quest height with the lowest strongly supported local surface; live door `Position`
  was treated as a floor and always received another 1.5-2 m flight clearance.
  A lower terrain layer cannot establish the doorway's height. The reported old
  stage target (554.4971,16.5,1473.821) is already about 1.75 m horizontally from
  the supplied doorway, so its different X/Z alone is not evidence of an axis bug.
- Preserve nonzero quest height; distinguish `EntranceIsFloor` from actual
  entrance world coordinates. Only sampled floors or quest/live-door origins
  matching the local floor within 0.5 m receive vehicle clearance. This preserves
  working floor-origin doors without treating every live-door origin as ground.
  Missing height uses a provisional player height/nearby terrain estimate until
  a live door resolves. A bounded measured correction uses Vector3(553.2,18.1,1475)
  only for accepted markers within 2 m in playfield 665. It is explicit user data,
  not an inferred correction for all missions. It stays authoritative across
  terrain/live-door updates. Other entries retain normal source resolution.
- Live-door association stays unique and bound to one identity. Measured/nonzero
  quest points use a 2 m horizontal association radius; zero-height unresolved
  map markers retain 6 m. Use remains within 2 m horizontally and 3.5 m in 3D
  of the live object. Normal live-door vertical tolerance stays 2.5 m; the supplied
  measured point permits up to 3.5 m to the object's origin, retaining its measured
  alignment height instead of descending toward a potentially offset model origin.
  Exact selected-mission dungeon verification, three attempts and timeouts remain.
- Select a 1.5 m approach side once near the entrance. Check both the travel segment
  and final short entry segment using centre/body-offset probes. Prefer the direct
  origin-facing side if clear; otherwise compare 16 sides for a viable final corridor
  and send only the selected point to the existing connected obstacle planner.
  Keep this side across probe retries; only changed entrance data or observed
  blocked movement can replace it. No viable alternative cannot rotate a committed
  anchor arbitrarily. Actual failure memory and progress limits remain active.
- Flight confirms actual 1-2 m radius and <=0.75 m horizontal error to the checked
  side before height alignment. Ground also stages at 1.5 m, confirming actual
  1-2 m radius and existing 2.5 m height tolerance before entry. Ground fallback now
  selects a clear forward waypoint first; otherwise its existing arc recovery runs.
- When entrance height is resolved, cruise uses a straight sloped approach at that
  target height. The planner adds climbs/around-object routes only for obstructions;
  unresolved far heights retain provisional elevated travel. Existing smoothing,
  braking, bounded connected search and advisory attempts remain. Never write player
  position/height or alter equipment. Dungeon exploration/combat/interior doors/
  lockpick/loot and accepted mission tracking are untouched.
- Logs use invariant decimal points and explicit X/Z/height(Y) labels for the raw
  marker, resolved entrance, waypoint and stage target. Source/diff/API review only;
  no local compilation, restore, tests or game run. User pulls main, builds and
  checks the exact measured entrance plus other missions and obstruction detours.
  Documentation updated; runtime success remains unconfirmed.

## Shared Outdoor Entrance Acquisition (2026-09-28)

This model supersedes earlier unique-door, exact nonzero quest-height, fixed
1.5 m side, 2/6 m door-search and three-minute/45-second final-approach rules.

- User asks to compare entrances that work/fail, fix marker-to-physical-door
  acquisition, tolerate zero/stale elevation and building offsets, try multiple
  door candidates/sides, preserve dungeon systems, update history and commit main.
  No compilation, package restore or local/in-game tests are authorized here.
- Baseline main is 4a822e1. AcceptedMissions copies Mission.Location.Pos from
  GetQuestWorldPos without axis conversion. AOSharp Vector3 is X/Y/Z, with Y
  altitude; AO /pos uses X/Z/height(Y). Reference code does not promise door,
  building/plot or exact threshold semantics, or an outdoor quest-door mapping.
  Available logs do not prove the prevalence of zero/stale height. Treating all
  marker types as exact was an assumption, not a verified SDK guarantee.
- Prior ResolveDoor searches only 2 m for nonzero/measured coordinates and 6 m
  for unresolved heights. Close ties return null; bound identity disappearance
  fails. One origin-facing 1.5 m approach and an actual 1-2 m annulus plus marker
  and live-door checks couple entry to layout offsets. Nonzero quest Y is
  preserved as exact. Working layouts can align all these assumptions; offset,
  multiple-door, obstructed-side, different-floor or proximity-trigger layouts
  can break them. Source-derived explanation, not confirmed per-mission telemetry.
- Earlier 11:08 searches exhausted ~4,040 probes with zero observed failed legs;
  the later measured PF665 doorway is (553.2,18.1,1475) internally. This supports
  investigating targets/probe gates. Keep the measured correction only as a
  local search/coarse hint for markers within 2 m there; live acquisition now
  determines final targets instead of freezing that measurement over a door.
- Coarse flight holds departure height instead of trusting stale quest
  Y; direct ground forward steps flatten Y before sizing the horizontal step.
  This avoids exaggerated flight altitude or negligible forward movement caused
  by an incorrect marker elevation. New EntranceAcquisition owns managed
  candidate/approach records and keeps no native pointers. Start within 48 m
  of the captured marker; scan every two seconds
  within 40 m horizontally, with no quest-height exclusion. Playfield.Doors
  already wraps AllDynels filtered by IdentityType.Door. Reject invalid/nonfinite
  objects; do not cast arbitrary props/items. Outdoor Door room links are null,
  so room data cannot rank mission association. Logs state this limitation.
- Rank anchor offset first, player distance/height and corridor hints next;
  open/locked flags and mission-like names are weak hints, never proof. Alternate
  candidates before repeatedly trying one identity; close-score ambiguity does
  not veto acquisition. Generate eight 1.5 m sides and four 3 m sides per height,
  based on rotation with player-facing fallback. Try live door origin, supported
  local floor/threshold (plus 1.5 m for flight), and nearby grounded-player height
  when materially different. Surface hits rank, not discard, these alternatives.
- Ground/flight use one acquisition state. Flight stages horizontally then aligns
  within 0.9 m of the selected approach and closes on its threshold; ground follows
  terrain, remembers completed staging and uses actual door range rather than
  requiring marker height. Existing ground/flight obstacle planners, braking and
  learned blocked geometry remain. Manual landing regenerates mode-specific
  geometry on the same mission without resetting global progress.
- Fresh lookup before every use; actual <=2 m horizontal/<=3.5 m 3D range and
  short corridor ending 0.6 m before the face. Use has no acknowledgement: log
  command sent, then observe zoning through the unchanged coordinator. Send two
  uses four seconds apart; if still outside after four more seconds, move 0.8 m
  past the threshold. After eight seconds from the last use, try another approach.
  Disappearance/moved origin, managed use errors and 18-second attempt stalls
  also yield to alternatives. Keep native objects only within the current tick.
- If no untried live door remains, visit marker plus eight directions at
  8/16/28/40 m with local floor/player height. Search stalls yield after eight
  seconds; newly loaded doors preempt the search. Marker arrival is never entry.
  Switching sides/phases does not count as progress; only new observed minima
  on finite attempts refresh the acquisition clock. Fail after all observed
  approaches/search points exhaust plus 90 seconds without progress. Existing
  15-minute total travel limit remains a separate cap. One failed door/use does
  not hard-fail; coarse travel still retains its existing 90-second progress bound.
- Exact selected-mission dungeon verification is unchanged. Candidate ownership
  cannot be proved outdoors; a wrong/unidentified dungeon remains refused after
  zoning. No changes to MissionDungeon, DungeonLayout, interior door/lockpick,
  combat, roller, map, loot, package versions or equipment handling.
- Logs cover captured anchor, candidate identity/type/properties/live position,
  offset/height delta, approach/source, range/corridor, sent use/no-zone/crossing,
  and alternate reason. README contains the source comparison and boundaries.
  Source/API/diff inspection only; the user pulls main, compiles and validates
  working/failing entrance layouts, ground/flight, stale Y and object refresh in game.

## Selected Accepted Mission Entrance Regression Fix (2026-09-28)

This supersedes the earlier 40 m generic-door scan, broad radial search and
horizontal-only selection rules in the historical shared-acquisition section.

- User reports that dynamic acquisition sent them to a shop. They require only
  entrances associated with accepted/uploaded missions, nearest active-mode
  mission selection from the player's origin, native map/minimap publication,
  bounded anchor acquisition/live height/alternate sides, detailed diagnostics,
  preserved dungeon systems, documentation and coherent commits to main.
  Do not compile or test locally; the user pulls, builds and validates in game.
- Baseline main: aa1e0af. The regression admits every Door within 40 m, including
  unrelated shops; name/proximity scores and post-zoning verification do not
  prevent the wrong interaction. The embedded Mali MissionView.PingClick already
  calls Mission.UploadToMap(identity); the AOSharp instance/static APIs forward
  that exact identity to GUIUnk.UploadMissionToMap. Reuse this interface.
- Selection compares only present unresolved-completion Rubi-Ka missions in the
  current playfield. Capture one finite origin, estimate all anchors in the active
  mode, use complete mesh distance where available for ground and horizontal direct
  fallback otherwise; flight horizontal cost avoids stale quest height. Mission ID
  breaks equal-cost ties. Only the winner gets terrain/final flight planning.
  These are mixed certified/estimated costs, not a guarantee of reachability.
- Before moving, refresh that exact live accepted mission/location and upload it.
  Cache uploaded ID/playfield/anchor to avoid per-tick calls; publish a new selected
  mission or changed anchor, and republish after outdoor zoning/reselection. No
  offered quest, custom marker, native interop or replacement renderer is used.
  Marker logging reports a sent native command; the API supplies no GUI readback.
  Changed accepted anchor (>0.5 m), removal or playfield invalidates travel for
  selection from the current player origin. Existing dungeon binding stays exact.
- Acquisition starts within 12 m; every door, approach and search target must stay
  within a fixed 6 m horizontal radius of the selected accepted anchor. No retry
  expands this radius. Candidate policy checks live Door type/validity/position,
  selected accepted status/playfield, optional QuestInstance and BuildingType/
  BuildingInstance via Dynel.GetStat, names and other accepted mission anchors.
  Reject positive conflicting quest IDs, ordinary shop/store/building/apartment/
  bar/club/bank/headquarters/transport context and unreadable context. Without a
  matching quest hint, nonzero building context is rejected conservatively.
- Stat enum/GetStat APIs exist in the supplied AOSharp sources and embedded roller
  enum. Their outdoor semantics are not fully documented: matching QuestInstance
  is an optional identity hint, always within the bounded anchor; no undocumented
  building codes or new pointer mappings are invented. Mission.Source/objective
  identities and outdoor null room links cannot prove entrance ownership. The
  radius/context fallback remains unconfirmed until exact dungeon verification.
- Refresh other accepted anchors while travelling. An unlinked door closer to or
  ambiguous with a distinct accepted anchor is rejected. Quest hints take priority;
  otherwise allow only the closest neutral threshold. Similar offsets (within 1 m)
  at distinct thresholds (>0.5 m apart) are held as ambiguous. Coincident anchors
  may share a threshold. Bind a chosen fallback threshold; refreshed identities
  must remain within 0.5 m there. Farther unlinked doors cannot follow a failed
  interaction or disappearance.
  A statless/unlabelled unrelated door inside the radius cannot be conclusively
  distinguished by this API; genuine unlinked building or >6 m entrances may be
  refused. Logs expose these limitations for user validation rather than widen.
- Retain eight 1.5 m/four 3 m approach sides per height, clipped to the original
  anchor radius; derive final height from live origin/local threshold/grounded
  player alternatives. Moving door origins rebuild clipped geometry. Marker and
  2/4/6 m search waypoints, PF665 measured hint and 0.8 m threshold crossing also
  stay inside the original boundary. Existing obstacle detours may leave the
  neighborhood for geometry but cannot introduce other candidate doors.
- Recheck association and geometry on refreshed live objects and force a full
  candidate scan/live accepted-identity check before every Use; include newly
  loaded competing doors. Preserve actual range/corridor checks, two uses per
  approach, finite retries, 90-second no-progress and 15-minute travel bounds.
  Log selected ID/origin/route cost/anchor, marker command, accepted and rejected
  door IDs/positions/context/reasons, final approach/height, sent use, absent
  zoning and exact verified/mismatched/unidentified dungeon or travel failure.
- Source/API/diff review only. No compilation, restore, tests or game execution.
  MissionDungeon, DungeonLayout, combat, interior doors/lockpick, embedded Mali
  UI/map/roller, loot, dependency versions and vehicle equipment are unchanged.
  README and conversation log updated. User pulls main and validates in game.

## Unified Local Outdoor Navigation and Persistent Directional Evidence (2026-09-28)

This section and the Intended Bot Workflow supersede all earlier outdoor planners,
small-side recovery, exact-marker elevation and arrival rules. Earlier sections
remain unchanged as the successful/failed revision history; do not stack them back
onto the new architecture.

- Compared main `f299006` against the complete recorded outdoor chronology,
  including mandatory mesh (`6e86c52`), hard flight/three ground recovery gates
  (`cd74489`), soft feasibility (`8a511a3`), successful nearby flight/vehicle entry,
  connected/prefix/outside-descent/footprint planners (`20dac70`-`bf05957`), precision
  steering/alignment (`6d41771`/`bb47f40`), measured/anchor confusion (`4a822e1`),
  the broad shop-door regression (`aa1e0af`) and mission-only association/map fix.
- The new request supplied mission 1442298255's 16:00 south/negative-Z and west
  approach evidence near X=630.91/Z=1416.02; east/+X was accessible per the user.
  `door=(None:0000)` gives no rotation. The old live-door-side label was false.
  These observations remain historical evidence, not invented verified successes.
  The PF665 measured point X=553.2/Z=1475/Y=18.1 also stays in history; current
  live geometry takes precedence and its hardcoded navigation override is removed.
- Consolidated `LocalMissionTravel` into CoarseTravel, ProbeExterior, AlignElevation,
  FinalApproach, Interact, CrossThreshold and AwaitTransition. `LocalMovement`
  handles observed Run/Fly movement; `LocalRoutePlanner` retains only estimates,
  optional complete mesh cost and local supported-floor/advisory-ray helpers.
  Removed connected flight searches, prefixes, probe-held execution, synthetic
  clearance launch gates, separate short ground recovery/flight execution layers,
  duplicate arrival/range/height plumbing and misleading inferred-live-door logs.
- Selection remains accepted/current-playfield only from the captured live origin.
  Run compares optional complete mesh cost with direct horizontal fallback; Fly
  estimates horizontal cost without demanding final height/clearance. Native
  `Mission.UploadToMap` remains, with fresh identity/location checks and delivery
  logs. No auto-rolling/acceptance, cross-playfield routing or equipment changes.
- Fly uses direct world-space travel at current departure height, lateral/raised
  alternatives after physical stalls, and precise vehicle steering/braking. Run
  uses optional mesh or progressive direct movement, with full heading/radius
  recovery based on elapsed stalls instead of a small lifetime attempt count.
  Geometry scores/logs do not forbid finite travel. No player position, speed,
  altitude or movement-state writes are added.
- Acquisition starts within outer movement radius +4 (24 m default). A fixed
  6 m mission-door association radius retains `f299006` quest/building/name,
  competing-anchor and ambiguity checks, including the bound neutral threshold.
  Larger movement rings never authorize farther doors or alternate buildings.
  Actual live identity/location/range/context and competing candidates are
  refreshed before Use. Keep <=2 m horizontal/<=3.5 m 3D use range and short
  pre-face corridor; two uses, four seconds apart, then bounded inward crossing.
- Probe 16 sectors (including cardinal/intercardinal) at preferred 12, outer 20
  and inner 6 m rings. Balance coverage, score observed progress/stalls and route
  around the anchor in committed ring segments. A blocked radial escape changes
  outward/tangential escape, rather than repeating it before every other direction.
  Try the other orbit direction/radius on later passes. Similar adjacent **actual
  player-side** stalls infer a likely face/corner and favor the opposite arc;
  requested candidate and actual blocked sector are separate diagnostics.
- Finite nondegenerate live rotation supplies both normal signs as strongest
  candidates; exterior sign still requires movement validation. Door geometry
  changes invalidate old plans. No Door means explicit inferred anchor-side
  labels. Newly acquired valid Door geometry replaces inferred thresholds.
  Sample supported local floor/player elevation; never trust stale quest Y.
  Live origin is primary, with flight clearance only when it matches a local floor.
  Alternate floor/player levels and lateral offsets remain runtime hypotheses.
- Stage about 3/1.5/0.4 m outside the live/inferred threshold, then short 0.8 m
  inward crossing/proximity trigger and wait. Run/Fly share the same sequence.
  Default leg stall is eight seconds; coarse net progress and stable candidate
  minima are bounded by 90 seconds. Full sector/radius coverage precedes an
  acquisition no-progress failure; fresh live geometry gets an attempt. Phase,
  target, equipment and retry changes never reset these clocks. Total local
  travel remains bounded at 15 minutes. Every failure logs a concrete reason.
- New `OutdoorNavigationSettings` loads `RKMissionData/navigation-settings.json`
  under deployed pluginDir. Defaults/ranges are documented in README; reload
  the plugin after editing. No new chat commands. Association radius is fixed.
- New `EntranceLearning` uses `RKMissionData/entrance-learning.json`: versioned
  plain managed records keyed by playfield +2 m quantized X/Z, with tightly bounded
  adjacent-cell matching. Optional mission IDs are per-attempt metadata, not the
  sole key; stale altitude cannot split an entrance. Retain mode, direction/angle,
  sector/radius, anchor/origin/candidate/final target, elevation source, Door
  identity/position/quaternion/forward/context, observed improvement/stall,
  use/crossing/zone evidence, timestamps and final results/reasons. Record coarse
  run failures, interruption and failed/mismatched/unidentified transitions too.
- Defaults cap 256 entrances and 96 diagnostic records each. Atomic replacement
  keeps one `.bak`; unsupported/corrupt files are preserved and write errors fall
  back to session observations with a log. Runtime files are ignored by Git.
  Reuse a remembered successful vector only in compatible mode/live geometry,
  validate it through current movement and association checks, then fall back to
  full probing if blocked. Historical failures are soft preferences, not bans.
- The coordinator now retains managed attempt evidence through TeleportStarted.
  Only the existing exact selected mission dungeon lookup, one-second stable
  dungeon and live room check records verified entry/last successful vector.
  Arrival/Use/zoning alone is never success. `MissionDungeon`, `DungeonLayout`,
  accepted tracking and all embedded exploration/combat/door/lockpick/loot/
  objective implementations are unchanged. Same-playfield confirmed-completion
  chaining and user-controlled dungeon exit remain unchanged.
- Source/reference/diff inspection only. Rechecked embedded Mali map upload and
  AOSharp Mission, Door, Quaternion and Playfield sources; outdoor raycasts use
  TilemapSurface and are incomplete scene evidence. No new SDK APIs/dependencies.
  No local compilation, package restore, tests or in-game run. User pulls main,
  compiles and supplies in-game directional logs and learning records; runtime
  correctness/reachability and undocumented quest-door stats remain unverified.

## Perimeter-bypass regression correction (2026-09-28)

- Main `c43c94e` correctly changed sector preference after a building face was
  inferred, but its committed fixed-radius arc still traversed blocked geometry.
  **Changing target sector without perimeter routing caused repeated wall-running.**
- Supplied 17:02-17:03 Fly log: mission 1442298255, PF665, anchor
  X=629.78/Z=1414.37, no live Door. FinalApproach from sectors 11 and 10 stalled
  near X=628.74/Z=1406.25 and X=627.64/Z=1406.29, radii 8.19/8.35 m.
  Opposite sector 3 was requested, but intermediate points
  (622.82,1404.59), (619.79,1407.72), (618.08,1411.71) scraped the west wall.
  Its actual stall was wall-bearing sector 9 at radius 10.30 m; requested sector 3
  never arrived. Later wall stalls reached radius 13.86 m, beyond the 12 m ring.
  Full original evidence is preserved under docs/navigation-evidence.
- Replaced the old precomputed arc with `EntranceOrbit`, a managed planner under
  the single travel/movement owner. Explicit OrbitBypass keeps a ring at observed
  wall radius +4 m, bounded by MaxProbeRadius, with approximately +/-1 m radial
  band, <=15-degree clearance-compensated tangential legs and outward correction.
  Widen by 3 m on stalls. Same/neighbor-bearing repeated stalls detect a face;
  stalled exterior routes also use this recovery. No parallel side-step engine.
- Probe both neighboring directions for up to 3 seconds when rays are equally
  clear/uncertain; rank observed angular/radial progress. Prefer remembered/shorter
  direction on ties; retain the opposite direction for fallback. Side arrival
  requires current target bearing, ring clearance, meaningful net bearing change
  (30 degrees minus arrival tolerance) and successful angular movement. Keep
  unwrapped travel span and net side change distinct. Retain wider-ring arrival
  X/Z during elevation alignment; only then resume the inferred/live final approach.
- Fly overpass is an optional fallback after side/ring recovery stalls: ascend
  at current X/Z to advisory sampled roof height in bounded 8 m steps (24 m max
  above bypass start), continue around the exterior and descend. Rays never reject
  the route. Blocked vertical movement is recorded; descent still needs actual
  exterior bearing/radius verification. In-game feasibility remains unverified.
- Coverage only records reached exterior candidates. Failed candidate routes
  retain requested sector and separate actual wall bearing/radius. Preserve
  angular coverage across retries, and stop exhausted perimeter recovery after
  elapsed no progress with an unresolved-route reason rather than claiming every
  side was reached. Total travel bound remains unchanged.
- Version-1 JSON remains readable. Store wall-bearing sectors, requested/reached
  side, CW/CCW, ring radius, span, overpass result and successful approach sector/
  vector. Only exact verified dungeon entry learns a successful bypass; compatible
  later runs prefer it and still validate current movement. Existing failed
  histories supply actual wall positions without reassigning requested sectors.
- Updated README and CONVERSATION_LOG. Source/API/diff review only: no compilation,
  package restore, tests or in-game execution. Accepted/current-playfield selection,
  native map upload, narrow live-door association, exact dungeon handoff and all
  dungeon exploration/combat/interior-door/loot implementations remain unchanged.

## Fly destination/obstacle/entrance separation (2026-09-28 follow-up)

- User tested 7a5ebaf and reported the same route, same stall and wrong chosen
  height. Requested the simpler sequence: enter zone, fly toward the designated
  mission, avoid interrupting objects by comparing over/around, diagnose the best
  entrance approach side locally, adjust the flight path and enter.
- Source inspection found Fly still inherited candidate floor height before
  exterior arrival and postponed overpass until perimeter widening/fallback
  failed. The old broad 17-column/multi-layer floor vote could override useful
  aircraft height and pick an irrelevant lower plane. This follow-up supplied a
  behavior report, not a fresh coordinate log; exact new in-game heights are unknown.
- `FlightPathPlanner` is now the single managed Fly transit/side-relocation
  planner under the existing travel/movement owner. Direct legs retain actual
  aircraft height. Five corridor rays and recorded movement failures compare
  local around choices and 6/12/24 m climb candidates immediately. Route cost
  accounts for path distance, obstruction hints, failed directions and revisits.
  Execute one leg, then replan from actual position. Never require clear rays.
- A completed climb keeps its higher altitude through the obstacle. Near the
  selected anchor, around choices use outward/tangential perimeter segments;
  an observed obstacle larger than the nominal ring expands both clearance AND
  the actual exterior goal. Default Fly radius bound is 36 m, climb ceiling is
  48 m above run-start aircraft height. Existing settings files get new defaults
  through property initialization; no user data is erased.
- Rank entrance sides by their own predicted inward corridor and local support
  or live Door height, on one adaptive Fly exterior rather than three nominal
  Ground rings. Coverage is tracked per movement mode and only records sides
  actually reached. If an associated live Door exists, only its geometry plans
  compete; do not endlessly retry an inferred target while a live Door is present.
  Side selection still observes requested vs wall bearings and actual arrival.
- Resolve final height after side arrival: associated live Door, compatible exact
  verified entry point, or first supported surface directly below reached exterior
  plus clearance. New Fly sampling uses five nearby columns (0.8 m), first hits
  only; the broad/lower-layer voter remains Ground-only. Align vertically at
  the reached exterior, then approach in short <=3 m Fly legs.
- Missing-Door height failures or crossings without zoning try offsets
  0/+2/+4/-1 m from that side's support plane, returning outside at current flight
  height before each alignment. Only after those hypotheses fail does the side
  yield. Fly transit probes stall at 4 s and entry probes at 3 s; Ground defaults
  remain unchanged. A plausible floor/roof is still not proof of a doorway.
- Persist entry point/height/source, rejected heights, actual reached exterior,
  route strategy/overpass outcome and the latest 12 completed/stalled flight legs
  per attempt, plus existing bounded sector/vector/radius/angular history.
  Exact dungeon verification alone learns the successful entry height. Keep
  older histories readable; do not seed success from reported observations.
- Removed Fly ascent/descent/fallback from `EntranceOrbit` and the old Fly fan/
  raised-step branch in `LocalMovement`; Ground recovery remains. Accepted
  selection/current-playfield filtering, native map upload, narrow Door checks,
  exact dungeon handoff and dungeon exploration/combat/interior-door/loot code
  are preserved. Source/diff inspection only; no compile, restore, tests or game run.

## Fly roof support and route reversal correction (2026-09-28, 18:01-18:03 log)

- Full user evidence is preserved in
  `docs/navigation-evidence/2026-09-28-pf665-flight-success-and-height.txt`.
  Main 4fbb10c did successfully fly to mission **1442298249**, PF665 anchor
  X=718.59/Z=1470.90, reach sector 14, descend to entry Y=20.50, zone and verify
  that exact mission in dungeon 14654046. Existing dungeon/ManagerLoot resumed.
  Preserve this behavior; do not hardcode its height for other mission anchors.
- Mission **1442298255** crossed inferred thresholds at Y=29.82 and 24.82, then
  its accepted destination was invalidated. No verified zoning is recorded;
  this is interrupted evidence, not success or proof of a selection defect.
- Mission **1442298208**, anchor X=710.81/Z=1567.06, selected south sector 12.
  At Y=37.67 it switched CW then CCW, returned to almost the initial bearing,
  and climbed to Y=44.94. The exterior's first support selected entry Y=38.82;
  crossing gave no zone, then the next hypothesis went UP to 40.82. Support is
  plausibly a roof/raised surface; the log does not prove this mission's true
  ground/door height. The user stopped before a verified entry.
- First-hit support is now checked farther outward on the REACHED side, in 4 m
  increments within the existing Fly radius bound. Two neighboring five-column
  patches must agree within 1.5 m and be over 3 m below local support. Missing
  local support may also use an outward supported patch. Move 2 m beyond the
  first matching patch at actual cruise height, then resample after arrival.
  Only then resolve entry height. No broad cross-face vote or through-roof
  lower-layer search is reintroduced. Hints remain advisory; actual movement
  decides. An elevated platform is not automatically declared a roof.
- Supported height alternatives are now 0/-1/+1/+2 m from clearance height,
  with the downward trial clamped to at least 0.5 m above support and duplicate
  trials removed. Unknown support tries 0/-2/-4/-8/+2 m from observed height.
  Associated live Door and compatible exact verified height retain priority.
- Fly around direction persists while its next corridor remains unobstructed.
  An actual stall, obstruction hint or changed goal releases it and retains the
  other direction for comparison. Clip short angular legs to the target bearing
  so the final leg does not overshoot and immediately reverse; the current
  reached point is excluded from revisit penalties for a short advancing leg. Over/around still
  compete; corridor hints do not hard-reject routes.
- The protected-radius test no longer classifies a radial outward escape as a
  side-to-side chord. A same-candidate entry return permits the sub-metre marker
  overshoot before moving outward. Clear rays and no recorded obstruction permit
  that return at current height, without the unnecessary +8 m climb seen in both
  failed missions. Side-to-side crossing safeguards remain.
- Persist support point/height/source and pending outward relocation alongside
  existing bounded flight/sector/height history; version-1 files remain readable.
  README and CONVERSATION_LOG distinguish verified success from unresolved entry.
  Changing a target sector without perimeter routing caused wall-running; choosing
  a local roof as the doorway floor then retrying higher caused roof-level loops.
- Source/diff review only, no local compile, restore, tests or game run. Accepted
  mission/current-playfield filtering, Door association and exact handoff remain;
  dungeon exploration/combat/interior-door/loot implementations are unchanged.

## Cruise clearance then 10 m mission-height match (2026-09-28, 18:23-18:24 log)

- User requires: gain enough elevation, within 10 m adjust to mission entrance
  height, then diagnose which side to approach and enter. Continue no local
  compilation/restore/tests or game execution; commit coherent changes to main.
- Full new evidence:
  `docs/navigation-evidence/2026-09-28-pf665-1823-height-drift.txt`.
  Mission 1442298249, PF665 anchor X=718.59/Z=1470.90, no live Door. Sector 14
  stalled around X=726.63/Z=1461.82/Y=18.26, radius 12.14 m. Sector 13 then
  selected entry 16.35 m and retried 15.35/17.35/18.35 m. It crossed at 18.35
  without zoning; sectors 3/4 selected still lower 15.50 m support. Sector 3
  stalled around Z=1479/Y=14.9-17.6. Stopped during sector 4, no verified entry.
  Earlier 18:02 log verified this same anchor at entry Y=20.50; that height must
  be reused when present in runtime learning, not hardcoded into source/data.
- The prior 91d5762 outward support correction could replace doorway height
  with lower surrounding terrain. Its initial surface/height was still tied to
  a particular reached sector. Removed outward floor search and per-sector
  support-derived heights. Preserve earlier revisions/records as failed evidence.
- Add explicit FlyClearance: advisory current/forward/anchor surface samples,
  default 6 m clearance plus transit tolerance, ceiling remains run-start +48 m.
  Retain already sufficient height. Unknown geometry requests +8 m; blocked
  ascent yields to the existing reactive over/around planner. No hard ray gate.
- Fly coarse target stops about 9 m from the anchor along the incoming bearing.
  Actual horizontal distance <=10 m halts transit and instantiates acquisition.
  Scan associated Doors but do not select/rank a sector until FlyMatchEntryHeight
  confirms actual height within 0.35 m. Ground's existing trigger is unchanged.
- One entrance-height plan per mission anchor: associated live Door, otherwise
  any compatible exact verified Fly entry point (not just the remembered sector),
  otherwise centre-supported marker-local plane + clearance. Only five nearby
  columns; highest supported plane first, up to four layers retained as fallback.
  Do not use broad 17-column/6 m voting or outdoor orbit terrain. Missing support
  uses explicitly provisional run-start aircraft height; quest zero is not used.
- Initial matching is vertical at the <=10 m arrival point. On observed block,
  score nearby descent corridors, relocate with the existing single Fly planner,
  retry the same height. May finish outside 10 m if the original column is blocked.
  Six observed recovery failures stop as unresolved height access, not a claimed
  entrance-side failure. All collision rays remain advisory.
- Side ranking and final approach share that mission height. A bypass can climb,
  then rejoin it at the actual selected exterior. Stalled approaches yield to a
  different side instead of replaying four low exterior-floor offsets. A
  completed crossing with no zone or reaching every sector without verified entry
  advances the bounded, mission-wide height plan
  (max six hypotheses) and re-matches before more side diagnosis. Trial index
  survives sector changes; associated live Door height has no speculative offsets.
  Keep a previously verified height until every sector has been reached; one
  unsuccessful crossing does not invalidate it. Provisional supported plans try
  observed lower marker planes before bounded +2/+4/+6 m clearance corrections.
  Coverage for the current height is distinct from historical sector coverage.
  Removed obsolete per-attempt height flags and return-path branch.
- Add cruise height, actual height-match point and 10 m trigger to managed records.
  Old version-1 records, including obsolete exterior support diagnostics, remain
  readable; no runtime data reset or synthetic success. Update README/history.
  Exact dungeon verification remains the only success gate. Accepted selection,
  current playfield filtering, map upload, conservative Door association, dungeon
  exploration/combat/interior-door/loot implementations are preserved.

## Smoother Fly cruise after verified entry (2026-09-28, 18:47-18:48 log)

- User reports improvement but too many stops/restarts and asks to extend travel
  distance. Preserve no local compilation, restore, tests or game execution.
- Full evidence: `docs/navigation-evidence/2026-09-28-pf665-1847-success-and-cruise-pauses.txt`.
  Mission 1442298249, PF665, anchor X=718.59/Z=1470.90. Initial distance 171.7 m;
  cruise reached Y=20.99, actual 10 m gate fired at 9.99 m, entrance height
  resolved to 20.61 m. Sector 13 exterior was actually reached, threshold crossed
  at Y=20.61, and exact mission verified in dungeon 14654046. Existing room/loot
  logic resumed. No observed stalls or blocked approaches in this run.
- Clear transit repeatedly finished 20 m legs. LocalMovement.Tick halted on
  arrival; Begin and SetPhase also halt before the next leg. The user's perceived
  pauses agree with this source behavior; logs do not time each individual stop.
  Two short right-side obstacle maneuvers and precise entry legs were intentional.
- Add FlightCruiseLegLength default 60 m, bounded 20-120 m. Only coarse Fly direct
  travel uses it. A hit on a long ray first reduces the horizon to the existing
  20 m neighborhood before choosing a detour; corridor hints remain advisory.
- At <=8 m remaining, at most every 250 ms, check a clear forward continuation
  while still farther than 18 m from the anchor. Extend an active level target
  only after >=2 m observed progress, within 15 degrees of its current direction,
  and with >=5 m additional remaining distance. Keep movement ownership, forward
  steering, last steering time and both observed/overall no-progress clocks.
  Do not call Begin/Halt/SetPhase or mark a continued leg Reached. Record the old
  segment as 'continued without stopping' in existing bounded flight diagnostics.
- Look-ahead checks do not mutate the active route strategy or committed bypass.
  Turns, climbs, descents, exterior/perimeter routing, actual 10 m entrance halt,
  height matching and final precision remain unchanged. No new recovery layer.
  Existing JSON files missing the setting use its property initializer default.
- Lesson: repeated waypoint completion can look like navigation recovery even
  when movement is progressing correctly. Retain the earlier wall-routing and
  entrance-height lessons and all previous evidence. Do not infer learned success
  from continued cruise; exact dungeon verification remains the success gate.
- README and conversation history updated. Source/diff inspection only, no local
  compile/tests/restore/game run. Accepted/current-playfield selection, map upload,
  live Door association and dungeon exploration/combat/interior-door/loot unchanged.

## 30% longer final Fly approach steps (2026-09-28, 19:03-19:04 log)

- User confirms smoother travel and requests about 30% improvement to the small
  entrance-approach adjustments. Interpret this as 30% farther per approach leg,
  not a promised 30% speed increase or relaxation of entrance precision.
- Full evidence: `docs/navigation-evidence/2026-09-28-pf665-1904-approach-steps.txt`.
  Mission 1442298249, PF665, anchor X=718.59/Z=1470.90. Cruise uses a clear 60 m
  leg, continuous 20 m horizons near obstruction and one right-side bypass, then
  a clear 56.01 m leg. Actual height gate fires at 9.85 m; remembered exact entry
  height 20.61 is reused. Sector 13 is actually reached. Five consecutive 3 m
  final-approach legs precede short staging legs and a crossing at Y=20.61.
  Exact dungeon 14654046 verified; existing exploration/loot resumes. No stalls.
- Change only Fly FinalApproach leg cap from 3 m to 3.9 m. Clamp every leg to its
  existing staging point using Toward, so close 3/1.5/0.4 m threshold targets are
  not skipped or moved. Log the 3.9 m cap when entering FinalApproach.
- Keep 60 m cruise, obstacle/perimeter routing, the 10 m gate, height matching,
  arrival tolerances, progress clocks, stall recovery, Door use/crossing and exact
  handoff unchanged. Ground and dungeon systems are untouched. Preserve earlier
  evidence and the lesson that short waypoint legs can cause visible pauses.
- README/history updated; source/diff review only. No local compile, restore,
  tests or game run, as requested. Commit and push coherent changes to main.

## Another 20% larger Fly steps and closer final staging (2026-09-28)

- User reports navigation is good, requests another 20% increase to final Fly
  approach steps and starting them within 5-7 m of the mission entrance.
- Increase Fly FinalApproach cap 3.9 -> 4.68 m. Choose 6 m as the closer staging
  target, measured from the resolved threshold (live Door when associated,
  otherwise mission anchor), along the confirmed candidate normal/lateral offset.
- Preserve the successful 10 m height check. After reaching a candidate exterior
  and AlignElevation, FlyCloseApproach executes one inward leg to the 6 m point
  before starting FinalApproach. Existing 0.8 m arrival tolerance gives about
  5-7 m actual distance. If already within 7 m, begin FinalApproach directly.
  Log the close staging point/sector and actual distance when final approach starts.
- Keep the wider obstacle bypass ring and learned wall/radius history unchanged.
  A close staging point is not a new exterior sector or a smaller bypass radius;
  an inward stall invokes the existing same-height side retry and wall attribution.
  Reaching staging is not verified entry. Preserve exact dungeon verification.
- Final 3/1.5/0.4 m threshold targets, height tolerances, progress clocks, cruise,
  obstacle routing, accepted/current-playfield selection and dungeon systems are
  unchanged. No new in-game evidence was supplied; this tunes the working sequence
  documented by the 19:03-19:04 log, without fabricating a new successful result.
- README/history updated. Source/diff inspection only; no local compilation,
  restore, tests or game execution. Commit and push coherent changes to main.

## Diagonal flight for every elevation section (2026-09-28)

- User adds a constraint: never navigate an ascent or descent exactly vertically.
  Applies to all outdoor Fly phases, including near-entrance height adjustments.
- One ElevationChoices helper in the existing FlightPathPlanner ranks eight
  horizontal directions at the requested height using corridor/failed-route hints.
  Initial clearance and overpass climbs now include forward/lateral movement;
  height matching and post-bypass alignment use outward/tangential slides.
- Horizontal run normally 2-16 m based on height delta. Near the mission, limit
  it to about a quarter of the current radius (minimum 2 m), clamp to the exterior
  bound and avoid chords inside the current/protected footprint. Filter degenerate
  projected points shorter than 1 m. Near-anchor initial clearance uses the same
  footprint protection until sufficient altitude is reached. Rays remain advisory.
- Descent recovery now ranks the actual diagonal height-change corridor plus
  the relocation corridor, replacing pure vertical-column scoring. The mission
  entrance-height target stays fixed across sectors and recoveries. No new planner.
- LocalMovement retains a horizontal heading and a small horizontal component
  if collision/rounding consumes X/Z before remaining elevation. This guard changes
  steering only; original target, arrival precision and progress clocks remain.
  One bounded log per leg reports a finishing correction. Full planned elevation
  logs include stage, actual source/target, horizontal run and height delta.
- Keep the working 10 m height gate, 6 m close approach, 4.68 m final cap,
  observed stall recovery, selection, Door association/use and exact dungeon
  handoff. Ground movement and dungeon exploration/combat/interior-door/loot are
  unchanged. Update current README/log wording that formerly promised vertical
  movement; retain historical attempts and earlier documentation in Git/history.
- Source/diff inspection only; no local compile, restore, tests or game run.
  No new in-game log or successful result is invented. Commit and push to main.

## User-confirmed navigation checkpoint and backup (2026-09-28)

- After diagonal-elevation commit 965003e, user reports: "seems navigation is good
  for now". Treat this as the current working navigation checkpoint and retain
  its implementation until a new issue or requested change is supplied. This is
  user feedback, not a fabricated new exact-entry log or learning record.
- Working behavior: clear Fly cruise uses 60 m horizons and continues through
  clear waypoints; obstructed long horizons narrow to 20 m before over/around
  recovery. All elevation sections include horizontal movement. Near an entrance,
  match the shared height after the actual 10 m trigger, confirm the exterior side,
  align diagonally if needed, then stage at approximately 6 m. Final steps are
  capped at 4.68 m and retain precise 3/1.5/0.4 m doorway staging and exact handoff.
- Preserve accepted/current-playfield selection, safe perimeter recovery, separate
  requested/observed wall-sector attribution, bounded entrance learning and all
  dungeon exploration/combat/interior-door/loot behavior. Earlier failed attempts,
  attached evidence, implementation history and conversation summaries remain in
  this repository and Git history. No more navigation tuning requested now.
- User requests saving memories/conversations on GitHub and a local repository
  backup under `C:\Users\Sumiko\OneDrive\Desktop\RK Mission Proj`. That directory
  already contains an older checkout. Create a separate dated folder
  `RKmission-backup-2026-09-28` there, preserving the existing checkout. Include all
  repository files and `.git` so commits, history and remotes are recoverable.
- Update this file, CONVERSATION_LOG and README, commit/push main, then copy that
  saved revision. Verify copied file contents and Git revision. Do not compile,
  restore packages, run tests or launch the game for this checkpoint/backup task.

## Mission readiness and 20 m engagement request (2026-09-29)

- Recovered GitHub memory/conversation summaries and prior chats read-only first,
  as requested. Confirmed main and the dated Desktop backup at checkpoint
  0350491; last outdoor code change is 965003e, accepted by the user. No new
  outdoor tuning is requested or included in this change.
- User testing feedback: pause at mission start if buffs/HP/nano are needed;
  use available healing/nano recovery after combat before proceeding to another
  fight, provided combat has not already resumed; narrow engagement to 20 m.
- Add MissionReadiness before new dungeon room actions, including while initial
  mesh generation is pending. Track player/pet combat and incoming aggro without
  using the engagement radius as a combat-state test. Aggro interrupts recovery;
  an established crossing retains ownership until safe room arrival.
- Default ready targets are 95% HP/nano, adjustable up to 100. Observe an existing
  CombatHandler for 3 seconds stationary at entry/after combat, then wait for
  pending spell/item/perk actions, observed outgoing cast/recharge horizons and
  a 2-second quiet gap. Preserve its configured profession buffs; never replace
  the handler or guess a buff loadout. Optional explicit BuffNanoIds requires
  actual active buffs (or stronger same-nanoline replacements).
- Recovery uses positive HP/nano effects exposed by usable main-inventory items,
  learned spells and available perks, following CastNano metadata to bounded depth.
  Check native self-use requirements, skill locks, cast state and action cooldowns;
  target self, serialize actions and avoid repeated active HoTs/health-damaging
  drains. Sit for treatment kits/passive regeneration and confirm standing before
  continuing. Unrecognized effect metadata remains with the combat handler.
- Readiness settings live in deployed RKMissionData/readiness-settings.json.
  No-progress (45 s) or total (180 s) exhaustion stops with diagnostic resource/
  buff state; it does not waive readiness or start the next fight. No new commands.
- A narrow Manager.Loot pause flag preserves its original process and settings
  while suspending updates during readiness/combat. Existing loot rules, room
  transitions, lockpicking, objectives and outdoor learning are preserved.
- All new enemy acquisition is within 20 m, including ordinary room NPCs that
  previously had no range gate. Retain an already active fight beyond that range.
  Large-room scanning moves within mapped room geometry before new acquisition,
  without targeting/attacking/sending pets early or declaring distant enemies clear.
- Asked which combat/buff plugin the user runs; no answer was supplied during
  implementation, so use the existing AOSharp handler and observable action state
  with an optional explicit buff list rather than plugin-specific private hooks.
- Source/API metadata and diff review only. No compilation, package restore,
  automated tests or game execution; user pulls main, compiles and validates.
  No new in-game success is claimed. Prior backup remains the previous checkpoint.

## Objective finale, automatic exit and local chaining (2026-09-29)

- User says the preparation/recovery changes seem good and asks to preserve them.
  Keep readiness settings/implementation, 20 m new engagements and the accepted
  outdoor navigation checkpoint. No additional outdoor tuning is requested.
- User requests retrieval of the five regular RK terminal types and completion
  ordering: find item, return item, repair/use item, find person, kill person.
  Reference: https://forums.funcom.com/t/rubi-ka-mission-settings-101/6664 and
  AO-Universe's how-to-pull-a-mission guide. Use the existing Mali icon mapping
  (11329/11330/11335/11337/11342) and native action identities, not name guessing.
- User clarifies that visiting the objective room last is conditional on layout
  feasibility, but completing its objective must always wait until every other
  enemy is dead. Avoid known objective rooms while alternate ordinary routes
  remain; allow required cut-throughs with exact objective identities held.
  Late-loaded objectives cannot have their room reserved before discovery.
- Managed MissionObjective preserves all seen steps/identities when native
  actions change or the quest disappears. Ordinary combat and both Manager.Loot
  candidate paths exclude held objectives. Reopen cleared rooms on new loaded
  enemies/loot; require all mapped rooms cleared and no skipped/unfinished loot
  before final actions. Early objective aggro/player/pet attack halts and recalls
  pets rather than allowing an early completion; external plugins/manual actions
  remain outside RKMission's ownership and may require user resolution.
- Find-item uses actual pickup/use, with objective container contents collected
  independently of the ordinary allowlist. Find-person observes first, waits for
  acknowledgement, then kills the NPC last and processes its corpse. Kill-person
  is acquired last at the preserved 20 m range. Repair uses the exact source item
  on the exact destination. Missing items/metadata/acknowledgement holds completion.
- Non-return automatic evidence combines our final action, pickup/source use/
  death/observation or recognized inbound server completion text and exact quest
  absence for two seconds. Outgoing manual Delete blocks the inference. The SDK
  has no exposed authoritative reward flag, so disappearance alone is insufficient.
- User explicitly supersedes the initial return-terminal travel proposal:
  **leave all return-item hand-ins manual**, because they are almost always in
  another playfield. After dungeon clearance, collect the item last, mark the
  bot run completed, retain ReturnHandInPending=true, exit and chain locally.
  Do not navigate to/use any return terminal or claim the game reward. Completed
  return quests remain excluded even if still accepted. Status is session-scoped,
  matching the previous manual completion tracker.
- Manager.Loot now records finished transfer/close processing separately from
  opened containers; keep ordinary rules/delete/reverse/quantity settings. Hold
  the exact objective container until the finale, capture collected contents and
  protect quest items from bag/reverse transfers. Skips hold automatic completion.
- Automatic exit reuses the mapped room crossings/blacklists/reverse cooldown,
  returns to the saved entry room and crosses identified external geometry with
  bounded waits. Can recover an entry room when starting inside. Missing geometry/
  blocked exit stops diagnostically rather than teleporting or assuming zoning.
  Reopen clearance if new enemies/loot appear while returning. Actual outdoor
  zoning starts the next closest eligible mission in the current playfield.
- Preserve /rkm complete [bound id] as an explicit manual status override; while
  armed it also requests automatic exit. Update README and conversation record.
  Source/API metadata and diff review only; no compile, restore, automated tests
  or game run. User owns build/in-game validation; no new successful run is claimed.

## Find-item selection and cleared-room passages (2026-09-29)

- User reports that a find-item mission repeatedly tried to use the object
  instead of targeting it. This corrects the pickup/use interpretation in the
  previous objective-finale entry: **find item selects the exact object**.
  Only explicit return-item collection uses pickup; all return hand-ins remain
  manual. Refresh pickup classification when return metadata arrives later.
- Retain the objective-last gate. Our final action plus exact item selection or
  recognized server completion evidence still needs disappearance of the bound
  quest for two seconds, with no observed manual deletion. Selection alone never
  completes the run. Keep find-item objects out of Manager.Loot,
  including exit's unfinished-content check, so an observation container cannot
  trigger repeated clearance after its mission is acknowledged.
- User reports repeated visits and stopping on every room entry. Room routing
  uses intermediate cleared rooms to reach unfinished branches/objectives or
  the exit; such backtracking is necessary in some layouts. Remove forced halts
  on cleared-room confirmation and at already open doorways; retain the safe
  interior position and 500 ms room identity confirmation. Keep an interior
  route active during that confirmation and issue the next doorway route in
  the same update after arrival, without repeating the two-second quiet wait.
  During the finale, available objective routes also continue immediately;
  unrelated edge cooldowns no longer delay them. Truly unavailable routes still
  wait for cooldown expiry or stop with a diagnostic if no route can recover.
- Still inspect live room contents and reopen ordinary clearance when needed.
  Keep readiness/recovery ownership at every confirmed arrival, combat/loot
  interruption, closed-door handling, failed-edge limits and reverse cooldown.
  Do not change outdoor navigation, 20 m acquisition, automatic exit/chaining,
  return-item manual hand-in policy or /rkm complete.
- Source/API/diff review only; no compile, package restore, automated tests or
  game run. Update README and conversation history and publish coherent main
  changes. User owns build/in-game validation; this is not a confirmed game run.

## Fix cleared-room invalidation and shorten reverse cooldown (2026-09-29)

- User asks to inspect the last two exploration iterations because cleared rooms
  keep being visited. Source review finds a regression introduced in 2555399 and
  retained by c807bb8: ReopenOccupiedRooms reused the broad combat-candidate
  query. That query admits the current fight, nearby attackers and hostile
  summons independently of room membership, so one fight could remove every
  cleared room from the clearance set. The previous pause fix missed this cause.
- User authorizes the necessary changes and explicitly requests a one-second
  reverse-edge cooldown. Introduce EnemiesInRoom as the existing combat query
  filtered by DungeonLayout.ContainsDynel for the specific room. Use it for
  cleared-room invalidation and remaining-enemy room selection. Keep the broad
  combat acquisition/defense query intact, including ongoing fights, nearby
  attackers, pets/sentries and the existing 20 m new-acquisition limit.
- Reopen only rooms containing an actual ordinary enemy or unfinished ordinary
  loot; log the room and enemy/loot identity when that happens. A fight in room 5
  must not mark already cleared rooms 1 and 2 uncleared solely because it is the
  current fight. Required mapped passage/backtracking remains allowed.
- Set ReverseEdgeCooldownSeconds=1 and derive both the expiry and log from that
  constant. Retain stable room-entry confirmation, exclusive crossing ownership,
  failed-edge blacklists and continuous cleared-room traversal. This supersedes
  the historical eight-second reverse-edge choice, not outdoor movement timers.
- Preserve readiness/recovery, objective-last completion, find-item targeting,
  manual return hand-ins, automatic exit/chaining and /rkm complete. Update README
  and CONVERSATION_LOG and publish coherent main changes. Source/API/diff review
  only; no build, restore, automated tests or game run. User validates in game.
