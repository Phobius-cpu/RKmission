# RKMission Conversation History

This log records user-visible RKMission conversation context. It intentionally excludes hidden system instructions, hidden chain-of-thought, credentials, and other private internal data.

## 2026-09-27

### User

> I want to create a bot for the game Anarchy Online that rolls missions in Ruby-ka and explores, kill all the enemies, and lockpicks doors and any treasure chests etc. in the missions it rolls. The project will be named RKMission as seens on the zip attached. The program that runs the bots/plugins is AO#. Save your memories and conversations on GITHUB.

Files supplied:

- `aosharp.newbots-master.zip`
- `aosp.knows-aosharp-mods-master.zip`
- `aosp-bots-master.zip`
- `malis-dungeon-map-2.0-master.zip`
- `malis-mission-roller-2.0-main.zip`
- `RKmission.zip`

### Assistant

> I’ll treat **RKMission** as the main project and the other ZIPs as reference implementations. I’ll inspect their structure/APIs first, then we can build the automation around mission rolling → travel/entry → dungeon exploration → combat → locked doors/chests → completion/exit, while keeping the AO# integration consistent with the code you supplied.

The RKMission project goal and supplied references were also saved to ChatGPT project memory.

### User

> you don't need to compile or test. I will pull, copile and test ingame and point out if any changes need to be made

### Assistant

> Understood. I’ll focus on **source changes only** for RKMission and won’t spend time compiling or testing unless you specifically ask me to.
>
> You’ll pull the changes, compile them, test in Anarchy Online, and send me any compiler errors, AO# exceptions, logs, or in-game behavior that needs correcting. I’ll use those results to iterate on the code.

### User

> Save your memories and conversations on https://github.com/Phobius-cpu/RKmission

### Assistant action

Confirmed access to the private `Phobius-cpu/RKmission` repository and created persistent project-memory and visible-conversation history files.

### User (continued in Codex)

> Inspect the uploaded ZIP archives and connected GitHub repo. Merge the relevant
> AO#/AOSharp, mission roller, dungeon mapping/navigation, combat, looting,
> lockpicking, and mission selection into RKMission. Do not compile or test
> locally; I will compile and test in-game. Commit coherent source changes.

### Assistant action

Inspected all six supplied archives and the repository. Replaced duplicate
placeholder classes with one AO# plugin, a zone-filtered mission roller, and
a dungeon room-clearing loop. Documented the cross-playfield navigation and
quest-completion checks for in-game follow-up. No local compile or test was
performed.

### User in-game feedback (continued in Codex)

> The bot doesn't seems to roll any missions, use malis-mission-roller to roll
> the missions. As for the navigation inside the missions use
> malis-dungeon-map2.0 to display the mission layout and the bot is stalling
> as seen (18:15) RKMission: Navigation stalled; stopped to avoid skipping a
> room. Pick the closest room available to avoid stall. For the portion part
> of the looting use the Manager.Loot found on aosp-bots to let me choose
> which items I want to loot. Also get rid of the AOSharp.NewBots.sln in the
> github repo.

### Assistant action

Used the uploaded mission roller, dungeon map, and Manager.Loot ZIPs as the
feature references. Updated the rolling request/response cycle; added the
dungeon layout display and graph-based room routing with a nearest reachable
fallback after a stalled edge; added per-character selected-item loot rules;
and removed `AOSharp.NewBots.sln`. No local compile or test was performed,
per the user's workflow.

### Compile inspection and interim UI change

The user reported that the prior revision would not compile. A later `main`
commit restored missing Mali map drawing helpers, removed an unused Nav package
reference, and added an RKMission window based on the earlier local roller and
loot-rule classes. That window is superseded by the original plugin UIs below.
The user still reserved compilation and in-game testing for themselves.

### User correction (continued in Codex)

> Integrate the actual Malis Mission Roller, Malis Dungeon Map 2.0, and
> Manager.Loot plugin code and interfaces into RKmission, rather than adapted
> replacements. Fix the back-and-forth loop at room doorways. Do not compile or
> test locally; commit the source and update the project history.

### Assistant action

Embedded the original plugin source and UI/config assets in RKmission, called
their classes directly from the bot, and removed the local roller, wall map,
and loot-rule replacements. Added only the hooks needed for RKmission's zone
rolling and room-scoped Manager.Loot operation. Doorway navigation now waits
for a stable destination room ID, times out by elapsed transition time, and
blocks failed edges before selecting another closest reachable room. No local
compile or test was performed, as requested.

### User in-game feedback (continued in Codex)

> The bot still gets stuck when entering a new room. Use the actual embedded
> Malis Dungeon Map interfaces, make room entry a dedicated transition,
> approach/open/lockpick the connecting door, cross to an interior point,
> suppress retargeting until confirmed entry, prevent immediate reversal,
> retry deeper and temporarily blacklist failed edges, preserve combat and
> loot integrations, add useful logs, and commit to main. Do not compile or
> test locally.

### Assistant action

Inspected the current navigation and embedded original Mali map factory.
Found that the crossing path still used `Room.Center` as a world destination,
although Mali's terrain builder treats it as tilemap coordinates. Replaced
that destination with points sampled inside Mali's original room outlines
in world coordinates. Added explicit doorway approach, opening/lockpicking,
crossing, deeper retry, stable target-room confirmation, reverse-edge
cooldown, and temporary edge failure blacklisting with nearest reachable
fallback. Kept Manager.Loot, combat, and objective handling after confirmed
room entry. Added logs for each phase and failure. No local compile or
in-game test was performed.

### User compiler feedback (continued in Codex)

> \"MainWindow\" é uma referência ambígua entre
> \"MaliMissionRoller2.MainWindow\" e \"MalisDungeonMap2.MainWindow\".

### Assistant action

Located the two unqualified `MainWindow.CurrentTerminal` references in
`RkMissionBot.cs`. Both original plugins contain a `MainWindow` class, and
the referenced `CurrentTerminal` member belongs to the mission roller.
Qualified both references as `MaliMissionRoller2.MainWindow` and checked for
other files importing both plugin namespaces. No local compile or test was run.

### User in-game feedback (continued in Codex)

> The bot got stuck, possibly because an object blocked its path to a corpse.
> It did not target an Alarm Sentry spawned by a security camera.

### Assistant action

Found an unbounded out-of-range corpse approach in `MissionDungeon` and a
combat filter that discarded pet-like and roomless spawned entities. Added
alternate standoff approaches, time and progress limits, a mission-scoped
Manager.Loot skip hook for unreachable loot, and logging. Expanded combat
selection to include nearby hostile spawns and Alarm Sentries while excluding
players and the local player's pets. Combat now interrupts an active loot route.
No local compile or in-game test was run.

### User in-game feedback (continued in Codex)

> The bot gets stuck trying to lockpick open doors, does not use the Lock Pick
> on locked doors, and ignores lootable containers inside rooms.

### Assistant action

Inspected the embedded AO# door flags and reference lockpick calls, plus the
Manager.Loot room filters. Added a brief crossing probe before a lockpick
attempt, refreshed the physical door each tick, sent the Lock Pick to the
`Door` object, and added a position-based fallback for door identification.
For room loot, both candidate selection and Manager.Loot's opening loop can
use Mali room geometry when `Dynel.Room` is missing or mismatched. Added
door and loot candidate logs. No local compile or test was run.

### User room-entry log (continued in Codex)

> Transition 2->6 reached an open door, then logged `still in room 6;
> pushing deeper` after an enemy attacked, and only then confirmed room 6.
> Minimize the stall without introducing navigation conflicts.

### Assistant action

Found that the stall retry condition could fire even after AO# detected the
target room. Started confirmation timing on the first target-room reading,
kept the safe interior and one-second confirmation checks, and limited
crossing retries to continued source-room detection. A target-room reading
near the doorway now continues inward without consuming a retry. Combat and
loot routing remain gated on confirmed entry. No local compile or in-game
test was run.

### User room-entry log (continued in Codex)

> Transition 1->5 detected the target room, then logged `room detected near
> doorway; moving farther inside` before confirming. The stall remains.

### Assistant action

Found a mismatch between Mali's selected interior waypoint (at least 2.5m
from the door with 0.8m wall clearance) and the player's confirmation limits
(more than 2.5m and 0.8m respectively). Normal arrival tolerance could stop
the player just short of confirmation. Relaxed the player's safe-entry margin,
shortened target-room stability to 500ms, kept movement active during that
check, and reserved deeper navigation for an actual stopped or stalled route.
No local compile or in-game test was run.

### User failed doorway log (continued in Codex)

> Transition 1->3 reached a door, then timed out after 30 seconds without
> any door action or crossing message and selected another room.

### Assistant action

Traced the silent `OpenDoor` branch: the reach check used a different distance
API from the threshold check and could keep navigating to the threshold
without ever attempting door interaction. Switched interaction approach to
world-space distance and the refreshed door position, bounded a stalled
approach at six seconds, and logged both distance readings at the doorway.
The original log does not contain those readings, so the exact discrepancy
will be visible on the next in-game run. No local compile or test was run.

### User room-choice and door-distance log (continued in Codex)

> Navigation seems okay, but room choice appears random and returns toward
> the origin. In transition 2->7 the bot reached a threshold 2.3m away while
> its refreshed door was 78.3m away.

### Assistant action

Found that the stored door identity was refreshed without a position check,
allowing a distant live door to redirect navigation away from Mali's mapped
passage. Kept the mapped threshold fixed and resolved the live door by proximity
to it on each tick. Made closest adjacent unvisited room the explicit first
choice; backtracking now occurs only to reach a more distant unvisited room
and logs that target. No local compile or in-game test was run.

### User entity discovery addition (continued in Codex)

> Also use Malis Dungeon Map plugin resources to identify all lootable
> containers and enemies within the mission.

### Assistant action

Inspected the integrated original renderer: it enumerates live AO# dynels and
uses simple-character and container identity types, while the map factory
provides room outlines. Added a shared room-mapped live entity view using
those outlines. Combat, Manager.Loot's room candidate and opening paths, and
mission objectives now use it; one survey per room logs visible enemies,
containers, and corpses. This covers entities as the client loads or spawns
them, since the plugin has no separate list of unseen entities. No local
compile or in-game test was run.

## 2026-09-28

### User compile error (continued in Codex)

> Fix C# CS0136 for `adjacent` after the navigation patch. Rename the
> conflicting variables without changing behavior, check nearby scopes,
> and commit to main. Do not compile or test locally.

### Assistant action

Located the enclosing nullable target and nested traversal variable in
`RKmission/MissionDungeon.cs`, `NextRoom`. Renamed them to `adjacentTarget`
and `adjacentCandidate` and updated their references. Inspected the full
method and surrounding transition methods for similar shadowing; no other
conflicts were found there. Navigation behavior and log output are unchanged.
No local compilation or tests were run; the user will pull and compile.

### User working-state feedback and backup request

> Bot seems good for now. Create a local backup at
> C:\Users\Sumiko\OneDrive\Desktop\RK Mission Proj, update the README on
> GitHub and locally with changes and relevant usage commands, and save
> our memories and conversations on GitHub.

### Assistant action

Reviewed the source and command handlers, updated the README with setup and
usage instructions, and recorded the user's working-state feedback. Corrected
outdated navigation, loot, map-window, and roller command descriptions. Saved
the synchronized repository source, embedded assets, documentation, and Git
history as the requested local backup. Personal runtime settings are separate.
No local compilation or tests were run.

### User front-half implementation request (continued from Organize RKMission conversations)

The user requested coherent source changes committed to GitHub main, with no
local compilation/testing. The revised flow lets the user roll/select any
number of accepted Rubi-Ka missions and travel between playfields themselves.
RKMission should take over in any matching outdoor playfield, choose the best
local entrance by route cost, support ground and flying-vehicle travel, land
and converge on precise door approach, verify dungeon handoff, and continue
with another local mission after completion. Preserve the working exploration,
room navigation, combat, door, lockpick, and loot systems; retain objective and
completion association without rewriting the dungeon logic.

### Assistant implementation

Replaced the automatic rolling/zone-selection coordinator with accepted-mission
snapshots and local takeover. Added terrain/navmesh route costs for all local
candidates and direct flying routes with climb, geometry clearance, descent,
and a reachable final ground leg. The default mode reads AO# flight state;
`/rkm travel auto|ground|flying` supplies overrides. Flight pauses at its landing
point for the user to dismount, then shares precise ground door approach.
Entrance attempts are bounded and ambiguous doors are withheld. AO#'s pinned
exact current-dungeon quest lookup verifies the selected identity before the
existing MissionDungeon logic starts.

One small dungeon integration hook refreshes/detaches the live quest reference
so removal does not leave a stale native mission pointer. The working room,
combat, door, lockpick, and loot code remains unchanged. Managed records retain
objective types and target/item identities, clearance, acceptance/removal, and
completion evidence. The SDK has no dependable reward/completion flag, so
removal and room clearance do not imply success. `/rkm complete [bound id]`
records the user's objective/reward confirmation. After the user exits, local
route selection resumes for another accepted mission in the same playfield;
otherwise automation disarms and inter-playfield travel belongs to the user.
Automatic exit traversal and objective-solver rewrites were not added.

Updated README.md and PROJECT_MEMORY.md with this responsibility boundary,
commands, navmesh requirements, session-only state, and recovery guidance.
Inspected the source and pinned API signatures only. No local compilation,
package restore, tests, or in-game run was performed; the user will pull,
compile, and validate in game. The older Desktop/OneDrive backup was not updated.

### User in-zone local-travel failure and fix request

The user reported RKMission armed in the mission playfield and detected four
accepted missions (1442233602, 1442233633, 1442233654, 1442233931), yet logged
`ground=unreachable, flying=unreachable` for every candidate and requested an
outdoor navmesh. They requested inspection of current main and AOSharp/reference
movement APIs, a viable direct/fallback ground approach, independent world-space
flight/landing, bounded recovery and useful logs, preserved dungeon systems,
updated documentation, and coherent commits to main. No local builds or tests;
the user will compile and test in game.

### Assistant diagnosis and source fix

Main's ground evaluator and every flight landing sample required a pathfinder;
flight also required a complete mesh path from landing to entrance. This shared
gate explains the reported rejection pattern in the correct playfield. The log
does not distinguish a missing mesh from disconnected/strictly rejected endpoints.
Inspected supplied AOSharp/newbots source and the installed pinned SharpNav SDK:
`SMovementController.SetDestination` supports direct waypoints without a mesh,
and the reference `BTBotBase` movement behavior uses that fallback. Nav destination
submission's bool does not guarantee a queued path.

Ground candidates retain complete mesh costs where available and otherwise use
direct distance estimates. Execution attempts short geometry-checked direct
waypoints, including precise entrance approach; stalled/rejected mesh movement
can fall back. Flight uses terrain/world-space approach samples without mesh
queries, retains clearance and altitude steering, and rechecks suitable terrain
before descent when near the destination. Provisional points allow approaches
to areas whose distant geometry has not loaded; they do not authorize blind landing.
The user still dismounts before ground door approach.

Added 12-second direct-step progress limits, at most three ground recoveries,
20-second flight target-progress limits, a 15-minute total local-travel limit,
and a two-minute dismount wait. Existing bounded entry and exact dungeon handoff
remain. Logs identify route type/reason, coordinates, actual movement state,
mesh availability, and recovery/failure details; repeated unchanged candidate
logs are suppressed. README now describes optional meshes and fallback limits.
Updated PROJECT_MEMORY.md with the diagnosis and reference/API evidence.

Dungeon exploration, combat, room doors, lockpicking, loot, and the coordinator's
verified dungeon handoff were not modified. Source/API inspection only; no
compilation, package restore, automated tests, or in-game run was performed.

### User playfield-665 feasibility/recovery follow-up (2026-09-28)

Continuing Organize RKMission conversations, the user reported working mission
tracking/distance ranking with no outdoor mesh, ground estimates about 32-100 m,
three side-step recoveries followed by an obstruction stop, and every flying
route rejected by climb/cruise/descent clearance even while already flying near
entrances. Requested soft feasibility, direct elevated flight with local descent,
multi-waypoint/arc ground recovery driven by actual progress/time, precise shared
entrance interaction despite zero/stale elevation, improved logs, updated project
history and coherent changes on main. User retains compilation/in-game testing.

### Assistant diagnosis and implementation

Inspected main 07fdbfa and its cd74489 local travel code, cached pinned SharpNav
1.0.44 movement/agent implementation, and supplied AOSharp/newbots references.
Flight selection required all synthetic climb/cruise/descent segments to clear;
execution also immediately failed any collision or missing terrain hit. Ground
counted useful detours against a lifetime three-recovery budget. Door lookup's
3D distance could exclude horizontally correct entrances with map height zero.
AOSharp uses Y altitude and horizontal X/Z; direct SetDestination does not need
a mesh, but ordinary waypoint arrival omits height, requiring explicit 3D flight.

Replaced flight clearance certification with a viable direct elevated approach
estimate for every finite entrance, actual movement attempts, advisory probes,
local descent near the entrance and bounded stall recovery. Local live door or
terrain height takes priority; a door loaded during descent upgrades the approach.
An unresolved height permits a short provisional descent and user landing, not
a route veto. Ground samples 16 headings at four radii, scores progress/clearance/
terrain/recent attempts, and progressively resamples from its new position. No
three-recovery cutoff remains. Eight-second waypoint stalls cause recovery;
90 seconds without new best final-target distance, plus a 15-minute overall
bound, determine movement failure. Recovery cannot refresh that progress deadline.

Both modes converge on horizontal mission-door lookup followed by precise live
3D door approach and bounded interaction. Unique entrance identity, nonfalling
ground state and exact selected mission/dungeon verification remain. Approach/
entry allows three minutes; door lookup 45 seconds; dismount two minutes and door
use three attempts/20 seconds. Logs distinguish estimates, active/final targets,
progress, advisory obstacles, recovery, descent/height source and HARD FAILURE.

Updated README.md and PROJECT_MEMORY.md. Working mission tracking, exploration,
combat, interior doors, lockpick and loot code were preserved; coordinator changed
only its idle route-selection wording. Source/API and diff review only. No local
compilation, package restore, automated tests or in-game testing were performed.
The user will pull, compile and validate these changes in playfield 665.

### User successful descent and nearest-entrance/vehicle-entry correction (2026-09-28)

The user supplied 07:09-07:10 logs showing flight descent reaching the final
approach, then a vehicle-dismount prompt followed by ground fallback obstacle
arcs near the entrance. They clarified: do not route multiple mission entrances;
estimate the closest from the origin and create that path only. A chosen flying
path must not require exiting the vehicle.

### Assistant follow-up changes

Capture one player origin, compare horizontal entrance distances, choose nearest
with mission-ID tie break, then plan only that mission in the active movement
mode. Store the chosen origin/entrance coordinates and keep mission selection
fixed during travel; state changes do not re-rank the mission list. Route costs
remain informational for the selected path. Later local-chain selections use
a new origin after user-confirmed completion and exit.

Removed the Dismount phase/wait. Flight descends near the chosen entrance,
continues precise approach with 3D flight steering, and attempts live door use
while Fly state and the vehicle remain active. No forced ground recovery or
exit-vehicle prompt exists on a flight route. If no unique live door is exposed,
continue to the selected entrance trigger point and await visibility/zoning,
retaining exact selected mission/dungeon verification. If the user manually
leaves flight near the entrance, continue that same mission on ground.

Updated README.md and PROJECT_MEMORY.md to replace route-cost ranking and user
dismount guidance. Preserved dungeon exploration/combat/interior-door/loot logic
and accepted tracking. Source/API/diff review only; no local compilation, restore,
automated tests or in-game testing. The user will pull main, compile and test.

### User entrance-height and flight-obstacle refinement (2026-09-28)

The user supplied 07:27 flight recovery/vehicle entrance-approach logs near
(699.33, 28.45, 1535.32). Pathing otherwise seemed fine. They requested refining
mission entrance height and avoiding buildings, trees and other objects ahead
by selecting a higher elevation or going around them. Keep the earlier nearest
entrance from one origin and flight entry without leaving the vehicle.

### Assistant refinement

Entrance floor is now resolved from multiple nearby vertical columns/layers,
with independent support and centre/inner-ring elevation, then refreshed during
local descent/approach as geometry loads. A unique live door overrides terrain.
The staging point's ground height no longer replaces the actual entrance floor.
Logs identify old/new floor, evidence source and the resulting hover target.

Flight checks a short speed-based corridor ahead with surface and native scene
line-of-sight queries, including vehicle-clearance offsets. A detected obstacle
can trigger a clear climb or lateral/raised waypoint before a stall; candidate
continuation and recent attempts guide selection. If no bypass clears, hold and
retry within the existing progress bounds. The selected mission, vehicle entry,
precise live door use and exact dungeon verification are retained. Height updates
and bypasses do not reset the final-target no-progress deadline.

Confirmed the additional APIs in cached reference source and pinned SDK metadata.
Updated README.md and PROJECT_MEMORY.md. Changes are confined to local planner/
travel and these history documents; ground recovery, accepted tracking, dungeon
exploration/combat/interior doors/lockpick/loot are preserved. Source/API/diff
review only; no local compilation, package restore, tests or in-game run. The user
will pull main, compile and test the two requested refinements in game.

### User lower-entrance loop and smoother-navigation follow-up (2026-09-28)

The user supplied 07:58 logs with FlightDescent at Y=39.87, final target Y=23.5,
19.7 m remaining and no improvement for 20 seconds. Avoidance attempts 36-38
selected lateral points at the same height. The entrance was below the flight
path; the bot seemed stuck trying to find it. Object avoidance seemed okay,
but the user asked to smooth the jerky path changes.

### Assistant descent and steering changes

Source inspection found no downward bypass candidates, immediate replanning on
any probe hit, full stops at waypoint arrival, instant direction changes, and
a hold that could resume movement between probe updates. Added descent columns:
move outside the obstruction at current altitude, recheck the actual alignment
position, then descend toward the lower approach in vehicle. Both legs need
local clearance; low-level continuation is preferred. General avoidance now
also has downward arcs. The selected mission and precise entrance interaction
remain fixed.

Clear waypoints persist until arrival, confirmed obstruction or stall. Two
consecutive hits confirm a replan; blocked movement remains held between checks.
Minor height updates preserve a committed bypass. Normal waypoint transitions
continue smoothly, direction changes are limited by elapsed time, and sharp
turns rotate in place while smaller moving turns check their actual path.
Logs identify alignment, controlled descent, lowering points and vertical gap.
Existing progress/time bounds continue without being refreshed by replanning.

Confirmed the smoothing APIs using cached source and pinned SDK metadata.
Updated README.md and PROJECT_MEMORY.md. Ground fallback, nearest mission
selection, accepted tracking, height refinement, vehicle entry and the working
dungeon exploration/combat/interior doors/lockpick/loot are preserved. Source/API/
diff review only; no local compilation, restore, tests or in-game run. The user
will pull main, compile and test the descent and smoother navigation in game.

### User short-hop regression and height-before-entry request (2026-09-28)

The user reposted 07:58 lateral avoidance 37/38 logs and reported worse pathing
than the previous version: characters move a couple of units instead of planning
a coherent path to the entrance. They requested selecting entrance height when
within 1-2 units, then proceeding to enter the mission.

### Assistant committed-path correction

Inspected main 141cf24 and replaced the single-bypass/two-leg recovery model
with complete waypoint sequences for the one chosen entrance. Compare direct,
raised/lateral/cruise/lower/approach and outward/down/return paths; prefer clear
whole paths, retaining estimates when geometry is inconclusive. Follow the full
sequence continuously and replan on actual sustained obstruction or waypoint
stall, with a retry interval. Removed speculative LOS-driven movement stops and
the rotate-in-place gate; moderate steering corrections remain smoothed.

The final sequence now travels to about 1.5 m outside the chosen entrance,
selects the floor/entry height within 2 m horizontally, aligns the character,
then enters in the vehicle. Live door height takes priority over nearby surface
consensus. Height is rechecked before use, and blocked alignment can use a
complete outward/down/return detour. Unique door, proximity entry and exact
selected mission/dungeon verification remain; no forced dismount.

Updated README.md and PROJECT_MEMORY.md with the new phases and diagnostic
logs. Preserved nearest-from-origin selection, accepted tracking, ground fallback
and working dungeon exploration/combat/interior door/lockpick/loot. Source/API/diff
review only; no local compilation, restore, tests or in-game run. User pulls main,
compiles and validates coherent flight travel and the near-entrance height sequence.

### User entrance-building obstruction follow-up (2026-09-28)

The user reports much better pathing but supplies 08:53 logs where flight plans
5/6 repeat the same one-leg estimate with eight advisory hints. EntranceHeight
is about 6 m from the goal, with just 0.3 m vertical gap. The actual mission
building is in the way. They request a route around the building/tree/cave.

### Assistant connected obstacle-route repair

Inspected main 20dac70 and AOSharp reference geometry/vector APIs. The planner
only compared fixed rectangles and lower-target ring templates, allowing a
known blocked direct leg to win again. Replaced templates with bounded connected
flight search at current, target and raised heights. It can combine several
corners around an object, then simplifies and commits the entire waypoint list
to retain the improved movement rather than reverting to short-hop replanning.

Remember observed failed movement directions, including objects missing from
surface data, and exclude known blocked segments. Keep the chosen mission viable;
if no complete route is found within the search bounds, hold/retry with a wider
margin under the original progress deadline. For final height alignment, allow
a clear point within 1.5 m of the same entrance at the already selected height,
so a blocked original-side point does not force travel through the building.

Preserved the within-2-metre height alignment, vehicle entry, unique door/exact
dungeon verification, ground fallback, tracking and working dungeon behavior.
Updated README.md and PROJECT_MEMORY.md and inspected the source/diff. No local
compilation, package restore, tests or in-game run; the user pulls main, compiles
and validates the building/tree/cave detour in game.

### User stationary flight launch regression (2026-09-28)

The user supplied 09:30-09:31 logs after the connected-search change. Mission
1442233633 is selected about 149 m away, in Fly state without an outdoor mesh.
The accepted entrance Y is zero, so initial floor/cruise elevation is provisional.
Searches repeatedly return no route with exactly 183 cells and 103 blocked edges,
even as margins widen from 32 to 80 m. The character never moves.

### Assistant flight launch and search-budget correction

Inspected main 29803a5. The planner required a whole path but only connected
cells to the destination within 12 m, discarded all usable sections at the
fixed probe limit, and counted budget exhaustion as obstruction. Cruise also
kept the distant provisional height until the final alignment phase.

Restored complete climb/cruise candidate routes and retained validated longer
sections when the final approach remains unresolved. Long cruise sections can
launch without proving the distant descent first; grid cells can connect to
the target at any distance. Reserve probes for path simplification so the result
keeps coherent movement. Follow a validated prefix and continue planning from
its observed endpoint without confusing it with the entrance or an obstacle.

Refresh the floor/cruise clearance as nearby live door/surface data loads; retain
height selection/alignment within 2 m and entry in the vehicle. Logs distinguish
complete routes, prefixes, actual arrival, physical blocks and search budgets.
Obstacle search, failed-direction memory, original progress bounds, mission
selection, ground fallback and working dungeon systems remain. Updated README.md
and PROJECT_MEMORY.md. Source/diff review only; no local compilation, restore,
tests or in-game run. User pulls main, compiles and validates launch/travel in game.

### User cruise arrival but blocked descent (2026-09-28)

The user supplied 09:54 Broken Shores logs. Mission 1442233602 travels about
104 m on a two-leg flight path and reaches the entrance horizontally. The floor
refines to 15.2; final height target is 16.2 while the character is at 40.625.
EntranceHeight repeats an empty search with 79 cells, 4,010 probes and 1,405
blocked edges, despite widened margins. Some missions still have pathing issues.

### Assistant outside-column descent repair

Inspected main 7c591ea. The high grid repeatedly probes all low doorway points,
has no intermediate lower planes, and rejects sideways/downward prefixes because
its score undervalues altitude loss. Added a dedicated search for a clear column
beside the building: move sideways at current height, lower there, then return
to the chosen entrance. Keep that coherent descent section even when the final
doorway connection is unresolved; continue planning from its actual lower endpoint.

Intermediate lowering planes allow staged descent where a full drop is blocked.
Generic fallback now scores actual 3D progress and permits necessary descent
detours. Logs show outside alignment/lowering, clear columns and the direct-drop
surface height. Existing vehicle entry, final height gate, floor/door identity,
timeouts, ground fallback and dungeon systems remain. Updated README.md and
PROJECT_MEMORY.md. Source/API/diff review only; no local compilation, restore,
tests or in-game run. User pulls main, compiles and checks this descent in game.
