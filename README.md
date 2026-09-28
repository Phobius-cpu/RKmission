# RKMission

AO# plugin for solo Rubi-Ka missions in Anarchy Online. It combines the original
Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot interfaces with
mission travel, room exploration, combat targeting, door handling, and looting.

**Status (2026-09-28):** the user reported useful flight obstacle avoidance,
with descent looping above lower entrances and jerky turns. The descent and
smoothing refinements
are source-only and await the user's build and in-game validation. Dungeon
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
   world-space travel toward an elevated point near the entrance, then prepares
   descent within 16 horizontal metres. Ahead checks select higher or lateral
   waypoints around detected obstacles while retaining the chosen mission.
   **Stay in your flying vehicle**: descent, the precise entrance approach, and door interaction
   continue in flight. Unresolved height allows a short provisional descent
   followed by entrance approach; there is no dismount pause or ground detour
   while you remain in Fly state.
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
- Flying cost estimates direct world-space travel to a point 4 m outside the
  entrance, at least 12 m above its estimated height, followed by descent/final
  approach. This cost describes the single chosen flight route and does not
  rank other missions. A synthetic climb/cruise/descent probe cannot discard it.
  Within 16 horizontal metres, the bot refines entrance height and prepares
  descent. A unique live door supplies the authoritative position/height.
  Otherwise, 17 vertical columns around the entrance sample up to four surface
  layers to reduce roof/canopy mistakes. A height needs at least three independent
  column votes, including nearby support; the centre or inner ring supplies
  precise elevation. Surface sampling repeats every two seconds as geometry
  loads, with a 0.5 m height tolerance. The staging surface four metres outside
  is kept separate from the entrance floor. A live door appearing later replaces
  terrain/provisional height. Without height evidence or a usable accepted height,
  a 4 m provisional surface target
  allows a small descent before continuing to the chosen entrance in flight.
  No mesh is needed. The approach side is anchored to the captured origin.
  Flight steering remains active through precise approach; door use is allowed
  in Fly state for a selected flight route. Manually leaving flight near the
  entrance can continue the same mission on ground; this is never required.
- Flight checks a short corridor ahead every 400 ms using surface rays plus
  native scene line-of-sight, with offset rays for vehicle clearance. Speed
  controls an 8-24 m lookahead. A detected obstacle triggers clear vertical,
  raised or lateral candidate legs, scored for useful continuation and recent
  attempts and turn size. Lowering arcs are also available. This can climb beside
  a building or arc around a tree before a collision/stall. If no local bypass
  clears, hold and retry within the existing
  no-progress limit; the mission remains selected. Only the final metre at the
  selected entrance is reserved for proximity entry. The climb ceiling is 40 m
  above the greater of initial cruise height and refined entrance height +12 m.
- A blocked descent toward a lower entrance first searches for a clear drop
  column: align at the current height outside the obstruction, then lower toward
  the resolved entrance elevation. Columns include the current/approach position
  and rings out to 28 m around the approach. Both alignment and full descent are
  checked before committing; descent is rechecked from the actual arrival position.
  Nearby ground can raise the drop endpoint to retain hover clearance. A clear
  low-level continuation is preferred, then precise entrance approach resumes.
- Clear waypoints remain committed until arrival, confirmed obstruction, or stall.
  Two consecutive ahead hits confirm a replan; a suspect/blocked corridor holds
  movement across intervening updates. Minor height refinements preserve the active
  bypass. Normal waypoint arrival no longer causes an unconditional full stop.
  Flight direction turns at up to 120 degrees/second; turns over 20 degrees rotate
  in place, and smaller moving turns check the actual heading for clearance.
  Waypoint arrival tolerates 1.2-2.5 m according to speed, followed by the actual
  descent recheck; precise entrance/door proximity checks retain their own limits.
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
  `Flight proactive avoidance` / `Flight obstacle bypass` for ahead checks
  and higher/lateral/lowering targets, `Flight obstacle recovery` for a stalled leg, and
  `Flight descent column alignment` / `Flight controlled descent` for the way down.
  Flight progress includes the vertical gap to the final target. Watch
  `Flight descent/final approach` for descent, then
  `Flight entrance approach in vehicle` for the last metres.
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
