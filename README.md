# RKMission

AO# plugin for solo Rubi-Ka missions in Anarchy Online. It combines the original
Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot interfaces with
mission travel, room exploration, combat targeting, door handling, and looting.

**Status (2026-09-28):** the reported doorway is X=553.2, Z=1475.0,
height(Y)=18.1 in playfield 665. A correction uses that measured point only for
mission markers within 2 m there. Valid quest heights are preserved, and terrain
floors no longer overwrite them or get confused with live door coordinates.
Both modes approach a checked point 1.5 m from the entrance before entry. Clear
routes stay straight; obstructions trigger committed detours and observed stall
recovery. This awaits the user's build and in-game validation. Dungeon
exploration, combat, room doors, lockpicking, and loot behavior are preserved; one hook refreshes
the live mission binding.

## Setup

- Use the classic client: the embedded Manager.Loot rejects the new engine.
- Compile `RKmission/RKmission.csproj` yourself. It targets .NET Framework 4.8
  and references AOSharpSDK, AOSharpSDK.SharpNav, and Newtonsoft.Json.
- Deploy the compiled output and dependencies through your AO# setup. Preserve
  the `Plugins/MaliMissionRoller2`, `Plugins/MalisDungeonMap2`, and
  `Plugins/ManagerLoot` folders beside `RKmission.dll`. The project copies
  their required JSON, UI, texture, and sound assets.
- Outdoor navmeshes are **optional**. If available, place them at
  `NavMeshes/<playfield id>.nav` beside the deployed plugin for full ground
  pathfinding. Without a usable mesh, AO# direct waypoints attempt bounded local
  ground approaches. Flight and descent do not require a ground mesh.
  Dungeon navmeshes are generated on verified entry.
- Keep a **Lock Pick** in normal inventory, enough lockpicking skill, and
  free inventory or configured backpack space.
- Open `/ManagerLoot` to choose loot rules. Review its reverse and delete
  settings; enabling Delete can remove items left in containers.

## Quick start

1. Roll and accept any number of Rubi-Ka missions yourself. Use Mali's original
   roller window, the game UI, or your preferred method. RKMission does not
   request offers, choose rewards/types, or accept missions automatically.
2. Check `/rkm missions`, configure `/ManagerLoot`, and use `/rkm start` to
   arm local takeover. There is no target-zone filter or RKMission roll limit.
3. Travel between playfields yourself by any means. When your current outdoor
   playfield contains accepted missions, RKMission captures your position as
   the origin, estimates horizontal distance to each entrance, and chooses the
   nearest (mission ID breaks ties). It creates one route from that origin to
   the chosen entrance, using the active movement mode. Other entrances receive
   no path/terrain/flight planning. Estimates do not guarantee full reachability.
4. Default `/rkm travel auto` reads AO# `MovementState.Fly`. Use
   `/rkm travel ground` or `/rkm travel flying` to override route selection for
   this plugin session. These commands do not equip or remove a vehicle.
5. On foot, use a complete mesh path if available, otherwise locally sampled
   waypoints/arcs around obstacles. In a flying vehicle, the bot attempts direct
   world-space travel along a committed waypoint sequence to a point about
   1.5 m outside the chosen entrance. Within 24 m, commit that point and the
   selected entrance height. A clear corridor can adjust height during travel;
   otherwise reach the coordinates at current clearance, then align height.
   Confirm actual position 1-2 m horizontally from the entrance and within
   0.75 m of the selected flight height before proceeding to entry.
   **Stay in your flying vehicle**: height alignment, approach and interaction
   continue in flight. Measured entrance coordinates take priority, then live
   door/valid quest coordinates; terrain heights remain estimates when neither
   is available. There is no required dismount or ground detour.
6. Both modes approach the chosen entrance and use a unique nearby live door
   when available. Without one, movement continues to the selected entrance
   trigger point while waiting for a door or zoning. AO# must associate the
   dungeon with the exact selected mission for a stable
   second before the existing `MissionDungeon` logic starts.
7. Check the game's objective/reward. Use `/rkm complete` (or
   `/rkm complete <bound mission id>`) to record confirmed completion, then
   **exit the dungeon yourself**. While still armed, the bot chooses the next
   nearest accepted mission from your new origin in that same outdoor playfield.
   If none remains, it disarms; travel to another playfield yourself and use `/rkm start` again.

Starting inside a mission uses AO#'s exact current-dungeon mission lookup;
it never guesses from mission-list order. Stop with `/rkm stop` whenever you
want manual control. Stop/start abandons the active run binding and permits
retrying an unfinished accepted mission.

## Travel and completion boundary

- Acceptance/removal is polled from `Mission.List` about once a second, even
  while disarmed. Only outdoor Rubi-Ka destinations from Mali's playfield
  catalog are eligible; unresolved/unsupported accepted records are retained
  for visibility, and unresolved locations are reconsidered on later polls.
  Roller filter settings do not restrict this collection.
- Each mission retains its identity, entrance, dungeon identity, action types,
  objective target/item identities, room-clearance state, and completion evidence.
  Removal is `RemovedUnconfirmed`; it can mean reward, deletion, or expiration.
- Selection uses distance estimates from one captured origin, not competing
  route costs. The chosen mission ID and entrance coordinates stay fixed while
  moving; a movement-state change cannot select another mission. An explicit
  stop/start or travel-mode change, removal, zoning, or completion can start
  a new selection. Only the chosen entrance is planned, in one movement mode.
- For the chosen ground route, complete navmesh path length plus endpoint
  approach supplies its path cost when available. Missing, partial, or
  disconnected meshes use the selected entrance's horizontal distance estimate.
  AOSharpSDK.SharpNav 1.0.44 `SetDestination` queues a direct waypoint without
  a pathfinder; `SetNavDestination` requires one. Ground fallback samples
  16 headings at 2/4/8/12 m, including tangent and backward arcs. Scores prefer
  progress, clear probes and plausible terrain; recent visited/failed points
  and a preferred detour side reduce oscillation. If every probe hits, a short
  cautious waypoint can still be attempted. Each reached/stalled leg resamples
  from the actual new position. There is no three-recovery limit.
- Flying travel estimates a route to a point 1.5 m outside the entrance,
  anchored to the captured origin. With a resolved height, prefer a straight
  sloped route instead of always climbing 12 m first. Only the selected mission is planned. The bot
  first tries direct and complete climb/cruise/approach paths. For obstructions, a bounded connected
  search links 4 m cells at current/target height and raised levels, up to the
  initial cruise/refined floor ceiling +40 m. It can follow several building
  faces, go around a tree, seek a cave opening, or climb over an object and return
  at entrance height. Clear stretches are combined into longer legs before the
  entire route is committed. Search margins expand from 32 to 80 m after retries;
  each attempt allows at most 450 expanded cells and 6,000 surface probes, with
  the search stopping around 4,000 to reserve probes for combining clear stretches.
  Cells can connect to a visible target at any distance, rather than only within 12 m.
  Body-offset hits rank clearance; missing geometry remains provisional.
  A known centre hit or observed failed direction excludes that segment, without
  rejecting the selected mission. If the final approach remains unresolved, keep
  a validated useful section of the path, then continue planning from its actual
  reached endpoint. Prefixes require at least 8 m horizontal or 6 m vertical
  displacement and a better estimated approach, or a clear launch climb while
  far from the entrance or a necessary outside drop. Progress scoring uses actual
  3D distance so vertical improvement is not outweighed by lateral displacement.
  A necessary climb or descent detour can temporarily increase final distance
  under the same progress deadline. Shorter samples are not committed as repeated
  micro-hops. Near the entrance, an inconclusive search can instead commit a
  bounded direct/arc movement estimate to the same stage target (see below).
  Hold/retry when neither a usable section nor such an attempt remains. Logs
  distinguish search limits from physical hits. Local searches cannot certify global access.
- The committed waypoint list is followed in order. New plans require
  changed destinations, eight seconds without waypoint progress, or sustained
  nearby surface obstruction; failed retries are separated by at least three seconds.
  Reaching a validated prefix immediately plans the next section, without treating
  that endpoint as the mission entrance or recording it as a failed direction.
  A short surface check runs every 400 ms and can pause a normally planned path
  for a nearby hit. On an explicitly advisory entrance attempt, surface hits are
  logged hints; observed failed movement still blocks the attempted direction.
  Scene/offset probe hints alone do not repeatedly stop moving characters. Normal
  arrival advances the existing path without stopping, after checking the next
  segment from the actual position to avoid cutting a corner. Moderate heading changes
  are smoothed, and sharp/corner-conflicting turns face the next leg directly.
  The former rotate-in-place gate is removed. Intermediate cruise arrival uses
  1.2-2.5 m according to speed. During entrance height alignment, reach an outside
  waypoint within 0.75 m before lowering; finish a lowering leg within 0.75 m
  vertically before returning sideways. Within 6 m of an alignment/entrance
  waypoint, movement checks run as often as every 25 ms on game updates instead
  of 100 ms, steer directly to that leg, and release forward when current velocity
  predicts arrival/overshoot. Resume movement after slowing if still outside the
  0.45 m waypoint tolerance. Cruise remains at 100 ms; ground/dungeon updates stay
  at 250 ms. This does not alter game speed or player position.
- Flight follows `FlightCruise -> EntrancePosition -> EntranceHeight -> EntranceApproach -> EnterDoor`.
  Within 24 m, commit one approach point 1.5 m outside the selected entrance.
  Check both travel to that point and its short final entry corridor with centre
  and body-offset probes. Keep the straight side when clear; otherwise compare
  16 sides once and choose an entry side for the existing connected detour planner.
  Reconsider the side after actual stalled movement, not on every update.
  Resolve flight height from measured/live door/quest coordinates or, when
  unavailable, a terrain estimate. Check the direct
  corridor once on starting the coordinate stage: clear data allows early height
  adjustment; otherwise keep at least current altitude/selected clearance while
  reaching the entrance neighborhood. Provisional height cannot alone reject
  this coordinate attempt. Keep the same approach side across probe retries;
  changed entrance data or an actual blocked leg permits another checked side.
  Actual horizontal distance between 1 and 2 m, within 0.75 m horizontally of
  the checked side, completes EntrancePosition.
  Then confirm that same distance range and <=0.75 m vertical error to complete
  EntranceHeight. An aligned observed position is not vetoed by a synthetic ray.
  Add vehicle clearance (radius +0.25 m, bounded to 1.5-2 m) only to a terrain
  floor or a quest/live-door origin matching that floor within 0.5 m. Preserve
  quest/live-door coordinates instead of overwriting them with terrain; other
  entry heights and the supplied measured point receive no extra offset. Use
  this same target for planning, alignment, proximity entry and live door drift
  checks. These stages plan only one coordinate/height goal; the former 16 extra
  doorway endpoints per search cell are removed. A staging prefix cannot replace
  the committed approach point or count as final alignment.
  Without a live door, final proximity movement uses <=0.45 m horizontal and
  <=0.75 m vertical error to the entrance trigger point, matching height alignment
  instead of chasing a tighter 3D height tolerance after coordinates are reached.
  Only then proceed to door/proximity entry. Height is
  rechecked before each use; drift or a new live door height returns to alignment.
  If the direct descent is blocked, a complete outward/down/return path can reach
  the alignment target. Before the grid search, sample 16 headings at
  4/8/12/20/28/40/56/72 m within the current search margin, testing the sideways
  leg at current height and the drop to selected entry height. Intermediate planes
  6/12/18 m below current altitude allow safe partial lowering around roofs.
  This search gets about 2,000 probes within the existing shared budget. Keep both
  sideways/lowering legs even if the final doorway leg is unresolved, then continue
  from the lower position. A detour away from the entrance is not a reason to
  discard an otherwise clear descent. Its staging column cannot change the chosen
  entrance floor or count as completed height alignment. This descent search
  also handles gaps over 1 m during final stages instead of only gaps over 3 m.
  Ground also stages at the 1.5 m approach, confirms actual 1-2 m proximity
  and its existing 2.5 m height tolerance, then enters. Its direct fallback
  chooses a clear forward segment first before sampling arcs.
  The entrance coordinate, mission and live door identity
  remain fixed; the vehicle stays equipped throughout.
- AO's displayed coordinate order is X, Z, height Y, whereas `Vector3` stores
  X, Y, Z. Logs now label these axes explicitly with decimal points and distinguish
  the accepted marker, resolved entrance, movement waypoint and stage target.
  The known playfield-665 correction is `(553.2, 18.1, 1475.0)` internally.
  It cannot affect markers more than 2 m away or other playfields. Nearby terrain
  cannot lower that measured height. A live door can refine other unresolved
  markers, but association is limited to 2 m for measured/nonzero quest points
  (6 m for unresolved zero-height map markers), with ambiguity checks retained.
- If normal surface search returns no usable section within 24 m, rank bounded
  estimates ending at the same coordinate/height target. Try direct and coherent
  outside/drop/return arcs at 4/8/12/20 m in 16 directions, at current/target
  altitude and +4 m. Allow up to 512 extra centre probes, scoring their hits as
  hints; actual failed legs/descents remain exclusions. `advisory=True` and
  `Flight committed attempt leg` distinguish these estimates from regular paths.
  Follow the entire sequence with the existing arrival/braking control, log
  nearby probe hints at most every eight seconds, and recover after eight seconds
  of actual waypoint stall. Estimates cannot bypass coordinate/height or door
  gates, reset progress limits, move the player directly, or change equipment.
  A probe-only pause replans without adding a falsely observed failed leg.
- An actual eight-second movement stall on a nearly vertical entrance descent
  learns an obstruction area around the stopping point, even if terrain rays
  report a clear drop. Start with a 4 m radius; another stalled descent inside
  that area at a similar height expands it by 4 m, up to 16 m. Avoid crossing
  the learned plane just below the stopped vehicle; sideways escape above it
  and a return underneath remain candidates. Both planning and execution respect
  this memory, including early waypoint transitions and smoothed turns. A probe
  pause alone cannot learn or enlarge the area. Memory belongs to this selected
  mission and clears on reset/new selection. Logs show held versus requested
  height, area radius and retained entrance height. The stopping altitude is an
  obstruction observation, not proof of the door's height; live door data still
  takes priority over the terrain estimate, and recovery retains the progress limits.
- Height sampling uses 17 nearby columns and up to four surface layers, requiring
  three independent supporting columns and centre/inner support. It refreshes
  every two seconds within 24 m during cruise/alignment/approach with a 0.5 m tolerance.
  A newly resolved floor can raise cruise clearance before final height selection.
  The within-2-metre alignment check still governs final interaction. Live door
  data overrides terrain; zero/stale accepted height remains provisional and
  cannot alone invalidate a mission. No outdoor mesh is needed. Manually leaving
  flight near the entrance can continue the same mission on ground.
- Ground probes remain soft hints. Ground/flight waypoint stalls of eight
  seconds trigger resampling; ground mesh movement stalls of 15 seconds
  trigger direct fallback. Recoveries and entrance-height corrections do not
  reset the final-target progress deadline: ground requires new best horizontal
  distance, flight new best 3D distance, within 90 seconds. Productive detours
  continue until the 15-minute overall limit. Precise approach/entry allows
  three minutes, unresolved door lookup/proximity entry 45 seconds, and door
  use retains three attempts/20 seconds. Client geometry queries and local
  sampling cannot guarantee a route around arbitrary terrain or large obstacles.
- AO# uses **Y for altitude**, with X/Z as the horizontal plane. Zero/stale
  accepted entrance height cannot alone invalidate a route or hide a door:
  nearby terrain/player height supplies provisional travel elevation, and door
  lookup uses a 6 m horizontal neighborhood. The live door's actual position
  and height still govern live-door interaction; ambiguous door use is withheld.
  Entry is limited to three uses and a bounded wait. A wrong or unidentified
  dungeon cannot start exploration. Interior room-door logic is unchanged.
- AOSharpSDK 1.0.106 exposes no dependable completion/reward flag. Room
  clearance, an objective interaction, and a disappearing mission are not
  completion evidence. `/rkm complete` records the user's in-game confirmation
  for the verified bound mission, including a removed mission. This pass does
  not add objective solvers or automatic exit traversal.
- Mission history and travel-mode overrides last for the loaded plugin session.
  Reloading rebuilds acceptance from the game; completion confirmations are
  not written to disk. No quest is deleted by RKMission.

## Commands

| Command | Purpose |
| --- | --- |
| `/rkm` or `/rkm status` | Show armed state, movement mode/phase, accepted count, bound mission/progress, and dungeon status. |
| `/rkm missions` | List tracked mission IDs, entrances/playfields, objectives, acceptance, and completion state. |
| `/rkm travel auto\|ground\|flying` | Set session travel mode; default auto uses the actual flight state. |
| `/rkm start` | Arm accepted-mission monitoring/local takeover, or verify and resume the current dungeon. |
| `/rkm stop` | Stop RKMission movement and dungeon automation. User-owned roller controls remain independent. |
| `/rkm complete [mission id]` | Record the user's confirmed reward for the verified bound mission; exit yourself to continue locally. |
| `/rkm zone <id>` / `/rkm rolls <count>` | Retired commands: display the new manual rolling/all-missions boundary. |
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

- **No rolling:** RKMission no longer initiates rolling. Use Mali's original
  window or the game UI to roll/accept missions yourself.
- **Waiting for local missions:** inspect `/rkm missions`, acceptance and
  resolved destination; travel to the matching outdoor playfield. `/rkm start`
  is required again after the local chain finishes.
- **Route diagnosis:** `Nearest entrance selected` logs the captured origin,
  chosen mission/entrance, estimated distance, single route/mode, path cost,
  movement state and mesh availability. There are no competing ground/flight
  plans for the mission list. Finite estimates remain attemptable even when
  clearance probes hit. Watch `Active movement` for the actual waypoint,
  `progress` for final-target distance,
  `Entrance height refined` for floor/source changes,
  `Flight path committed` for the entire waypoint sequence and replan reason,
  `complete=True/False` for complete routes versus validated sections,
  `Flight committed path leg` / `Flight committed prefix leg` for progress through that sequence, and
  `Flight blocked leg recorded` / `Flight obstacle search waiting` for observed
  obstruction and bounded search retries. `advisory=True` / `Flight committed attempt leg`
  identifies an attempted estimate when surface search is inconclusive. Watch
  `outside descent prefix` / `outside descent and entrance return`,
  `outside alignment for descent` and `descending to approach height` for roof avoidance.
  The descent search reports clear columns/drops and the direct-drop surface Y.
  Watch `Entrance approach point set`, `Entrance coordinates reached`, then
  `Entrance coordinates and height aligned` before entry. These logs show the
  committed point, actual entrance distance and vertical error. An early clear
  corridor can still adjust height during coordinate travel. Selection logs include
  vehicle radius/clearance; flight progress
  includes the vertical gap and velocity. Movement labels use the committed
  leg's starting altitude so slight corrections cannot alternate/log every tick.
  Idle selection checks retry every five seconds; mode warnings are suppressed
  when unchanged. During movement, the selected mission remains fixed.
- **Fallback stopped:** `Local travel HARD FAILURE` identifies an actual
  timeout/no-progress, invalid coordinates/state, or entrance failure. Three
  obstruction probes no longer stop a run. Move around the obstacle or closer
  to the entrance, then `/rkm start`. An optional outdoor mesh improves difficult ground routes.
  Do not assume a direct estimate guarantees passage through every obstacle.
- **Vehicle entry:** flight routes continue to the entrance with the vehicle
  equipped. No dismount prompt/wait exists. Mode selection does not change
  equipment. Explicit ground mode requires actual ground movement.
- **Entrance/handoff failure:** read the coordinate, door identity, route cost,
  and entry logs. Ambiguous doors or an unmatched dungeon stop/hold entry.
- **Clearance without confirmed completion:** check the objective/reward in
  game and use `/rkm complete` for that bound mission. Deleted/expired missions
  are not automatically marked complete; stop/start to abandon that binding.
- **Map missing:** the original map recommends the launcher's
  `Direct 3D T&L HAL` graphics setting.
- **Door failure:** check the transition log, Lock Pick, and skill. Temporary
  route blocks may expire and allow another attempt.
- **Skipped loot/items left behind:** check skip logs, rules, and free space.
  An empty rule list can still open containers; transfers follow loot settings.
- **Stopped run:** read the reason in chat, including death, unreachable
  enemies, failed routes/missing geometry, missing objective items or objectives,
  and AO# exceptions.

The user owns rolling, selection, inter-playfield transport, vehicle equipment,
reward confirmation, and dungeon exit. RKMission owns local route selection,
travel/door entry, verified dungeon handoff, and same-playfield continuation.
The working dungeon systems continue to own exploration, combat and loot.

## History and local backup

`PROJECT_MEMORY.md` stores durable project context and `CONVERSATION_LOG.md`
stores user-visible conversation summaries. GitHub `main` is the source of truth.
The prior source backup (created before this travel revision) is at:

    C:\Users\Sumiko\OneDrive\Desktop\RK Mission Proj

That backup is not refreshed by this revision; pull GitHub `main` for the new
source and usage notes. The user compiles and tests in AO#; no local compilation
or tests were run for this update.
