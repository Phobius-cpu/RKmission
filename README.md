# RKMission

AO# plugin for solo Rubi-Ka missions in Anarchy Online. It combines the original
Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot interfaces with
mission travel, room exploration, combat targeting, door handling, and looting.

**Status (2026-09-28):** the user reported working dungeon behavior before this
travel revision. The local-travel feasibility/recovery revision is source-only and
awaits the user's build and in-game validation. Dungeon exploration, combat,
room doors, lockpicking, and loot behavior are preserved; one hook refreshes
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
  ground approaches. Flight and landing do not require a ground mesh.
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
   playfield contains accepted missions, RKMission evaluates every local
   entrance and ranks routes for the active movement mode. Direct ground costs
   are estimates for an attempted approach, not proof of full-path reachability.
4. Default `/rkm travel auto` reads AO# `MovementState.Fly`. Use
   `/rkm travel ground` or `/rkm travel flying` to override route selection for
   this plugin session. These commands do not equip or remove a vehicle.
5. On foot, use a complete mesh path if available, otherwise locally sampled
   waypoints/arcs around obstacles. In a flying vehicle, the bot attempts direct
   world-space travel toward an elevated point near the entrance, then prepares
   descent within 16 horizontal metres. Collision probes advise recovery;
   they do not reject the mission or stop moving characters. **Dismount when
   prompted**; ground approach resumes once flight/falling state clears. If
   local height remains unresolved, the bot makes a short provisional descent
   and asks you to land/dismount before the precise door approach.
6. Both modes approach a unique nearby entrance door and attempt entry. AO#
   must associate the dungeon with the exact selected mission for a stable
   second before the existing `MissionDungeon` logic starts.
7. Check the game's objective/reward. Use `/rkm complete` (or
   `/rkm complete <bound mission id>`) to record confirmed completion, then
   **exit the dungeon yourself**. While still armed, the bot chooses the next
   cheapest accepted mission in that same outdoor playfield. If none remains,
   it disarms; travel to another playfield yourself and use `/rkm start` again.

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
- Ground cost uses complete navmesh path length plus endpoint approach when
  available. A missing, partial, or disconnected mesh falls back to a direct
  horizontal distance estimate, preserving that mission as an attemptable candidate.
  AOSharpSDK.SharpNav 1.0.44 `SetDestination` queues a direct waypoint without
  a pathfinder; `SetNavDestination` requires one. Ground fallback samples
  16 headings at 2/4/8/12 m, including tangent and backward arcs. Scores prefer
  progress, clear probes and plausible terrain; recent visited/failed points
  and a preferred detour side reduce oscillation. If every probe hits, a short
  cautious waypoint can still be attempted. Each reached/stalled leg resamples
  from the actual new position. There is no three-recovery limit.
- Flying cost estimates direct world-space travel to a point 4 m outside the
  entrance, at least 12 m above its estimated height, followed by descent/final
  approach. Every finite local entrance retains a flight estimate; a synthetic
  climb/cruise/descent probe cannot discard it. Within 16 horizontal metres,
  the bot resolves local terrain/live door height and prepares descent. A live
  door loaded during descent replaces a provisional/terrain approach. Without
  height evidence or a usable accepted height, a 4 m provisional surface target
  allows a small descent before requesting user landing. No mesh is needed.
  Both alternatives are calculated; active mode selects ranking, with mission
  ID as tie-breaker. Landing near the entrance joins the same ground approach.
- Collision/terrain probes are soft hints. Ground/flight waypoint stalls of
  eight seconds trigger resampling; ground mesh movement stalls of 15 seconds
  trigger direct fallback. Flight recovery samples lateral/raised waypoints
  with a ceiling 40 m above the initial elevated target. Recoveries do not
  reset the final-target progress deadline: ground requires new best horizontal
  distance, flight new best 3D distance, within 90 seconds. Productive detours
  continue until the 15-minute overall limit. Dismount waits two minutes;
  precise approach/entry allows three minutes, unresolved door lookup 45
  seconds, and door use retains three attempts/20 seconds. Local sampling
  cannot guarantee a route around arbitrary terrain or large obstacles.
- AO# uses **Y for altitude**, with X/Z as the horizontal plane. Zero/stale
  accepted entrance height cannot alone invalidate a route or hide a door:
  nearby terrain/player height supplies provisional travel elevation, and door
  lookup uses a 6 m horizontal neighborhood. The live door's actual position
  and height still govern final interaction; ambiguous doors are withheld.
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
- **Route diagnosis:** candidate logs include playfield, player position,
  actual movement state, mesh availability, costs, and route reasons. With no
  mesh, expect `ground=... (no outdoor mesh; direct estimate)` and
  `flying=... (world-space flight estimate; elevated approach, local descent on arrival)`.
  Finite estimates remain attemptable even when clearance probes hit. Watch
  `Active movement` for the actual waypoint, `progress` for final-target distance,
  `obstacle hint/recovery` for advisory probes/resampling, and
  `Flight descent/final approach` for the locally resolved landing target.
  Idle checks retry every five seconds and suppress unchanged candidate logs.
- **Fallback stopped:** `Local travel HARD FAILURE` identifies an actual
  timeout/no-progress, invalid coordinates/state, or entrance failure. Three
  obstruction probes no longer stop a run. Move around the obstacle or closer
  to the entrance, then `/rkm start`. An optional outdoor mesh improves difficult ground routes.
  Do not assume a direct estimate guarantees passage through every obstacle.
- **Landing pause:** dismount when prompted. Mode selection does not change
  your vehicle or force a flight-state switch.
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

The user owns rolling, selection, inter-playfield transport, vehicle dismount,
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
