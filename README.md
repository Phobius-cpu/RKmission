# RKMission

AO# plugin for solo Rubi-Ka missions in Anarchy Online. It combines the original
Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot interfaces with
mission travel, room exploration, combat targeting, door handling, and looting.

**Status (2026-09-28):** outdoor mission coordinates are search anchors. Ground
and flight now share live door acquisition, alternate approach sides/heights,
identity refresh, and bounded recovery across candidates. A nonzero quest height
is also provisional. Entry still requires exact mission/dungeon verification.
The user's build and in-game validation are pending; no local compilation or
tests were run. Dungeon exploration, combat, interior doors, lockpicking and loot
are unchanged by this entrance fix.

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
5. On foot, use a complete mesh path when available, otherwise sampled local
   waypoints/arcs. Flight follows committed world-space waypoints. Within 48 m
   of the marker, both modes scan live doors within 40 m of that anchor. They
   rank candidates and approach sides, using live origin and local threshold
   height alternatives instead of demanding the marker's exact X/Y/Z.
   Flight aligns to a selected side/height before closing on the threshold;
   ground follows terrain and checks the live door's actual interaction range.
   **Stay in your flying vehicle**: the bot does not change equipment. Manual
   landing near the anchor continues the same mission through ground acquisition.
6. Door use refreshes the live identity and checks actual distance and the short
   interaction corridor. No transition after use triggers a short threshold
   crossing, then alternate sides/doors. Without live doors, bounded radial
   waypoints search around the marker. Arrival or a use command alone never
   proves entry. AO# must associate the dungeon with the exact selected mission
   for a stable second before the existing `MissionDungeon` logic starts.
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

- Acceptance/removal is polled from `Mission.List` about once a second. Only
  resolved outdoor Rubi-Ka destinations are travel eligible. Removal can mean
  reward, deletion or expiration; it is not proof of completion. Identity,
  objectives, dungeon binding and completion evidence remain attached to the
  selected accepted mission.
- Nearest selection compares horizontal distance from one captured origin.
  Only that mission is planned. Its captured marker and mission identity stay
  fixed during travel; live entrance targets can change without selecting a
  different mission. Completion confirmations and travel settings are session
  state; no quest is deleted.
- Ground uses complete navmesh paths, then local direct waypoints if the mesh
  is absent/disconnected. Fallback samples multiple headings/radii and remembers
  visited/failed points. Flight retains the connected obstacle/descent planner,
  committed waypoint execution, braking and observed blocked-leg/descent memory.
  Surface probes rank approach alternatives; synthetic hits do not discard the
  mission. Inconclusive final searches can attempt bounded advisory paths that
  still exclude observed failed movement. No direct player position, altitude,
  speed or movement-state writes are introduced.
  Coarse flight holds departure height until acquisition (obstacles can require
  climbs); stale quest Y cannot command an early climb/descent. Direct ground
  forward steps use horizontal distance before sampling local terrain, so stale
  Y cannot shrink a normal step to a negligible horizontal movement.

### Marker versus physical entrance

`AcceptedMissions.Refresh` copies `Mission.Location.Pos` from AOSharp's
`GetQuestWorldPos`. The reference API exposes a quest world position, with no
contract that it equals the physical door, threshold, building centre or plot
centre, and no outdoor door-to-quest mapping. The available logs do not establish
how often each layout supplies an approximate anchor or zero/stale elevation.
We therefore treat every marker as an outdoor search anchor rather than claim a
specific interpretation for all missions. AOSharp uses X/Y/Z with **Y altitude**;
AO displays X/Z/height(Y). A reported Z=0 needs this distinction checked first.

Source inspected: current RKMission main `4a822e1`, earlier entrance history and
[AOSharp Mission](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Mission.cs),
[DynelManager](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Dynel/DynelManager.cs),
[Door](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Dynel/Door.cs),
[Dynel](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Dynel/Dynel.cs)
and SimpleItem reference sources. `Playfield.Doors` already derives from live
`AllDynels` with `IdentityType.Door`. Outdoor room links return null; they cannot
prove a mission association. Names, open/locked flags and collision probes are
ranking hints. Arbitrary scenery/items are not cast to Door. `Use()` sends a
command without a success acknowledgement. Exact association is established by
the existing current-dungeon mission lookup after zoning.

| Earlier behavior | Why an entrance could work | Why another layout could fail |
| --- | --- | --- |
| Door scan within 2 m of a nonzero/measured marker, 6 m for unresolved height | Live door coincides closely with marker | Offset building/cave door excluded; measured point freezes lookup |
| Nonzero quest height treated as exact | Quest height matches doorway | Stale elevation forces wrong alignment; zero height takes a different path |
| One bound identity; near ties return no door | One visible door | Multiple doors never resolve; disappearance stops entry |
| One 1.5 m side and mandatory 1-2 m annulus | Outside side is accessible | Door inside facade, roof, slope or offset origin blocks that point |
| Marker range plus door range; one failed use sequence stops run | Both coordinate checks happen to agree | Player reaches real door but fails marker/height gate or needs trigger crossing |

Earlier 11:08 logs showed height searches exhausting roughly 4,040 probes with
zero observed failed legs. The user later measured X=553.2, Z=1475.0, height=18.1
in playfield 665. Those observations support a target/probe mismatch as a possible
cause, not proof of every building's marker semantics or a universal correction.
The measured point is retained only as a local coarse/search hint for markers
within 2 m there; it cannot override the selected live door or all other missions.

### Shared entrance acquisition

- Start within 48 m horizontally of the captured anchor. Scan every two seconds
  for finite live Door positions within a 40 m horizontal radius, independent
  of quest elevation. Record identity, position, anchor offset, name/type,
  open/locked flags, outdoor room-data unavailability and accessibility score.
  Anchor distance dominates ranking; player distance, height difference and
  corridor probes refine it. No close-tie ambiguity veto remains.
- For each observed door, generate eight sides at 1.5 m and four at 3 m,
  oriented by its live rotation, with player-facing fallback if rotation is
  unusable. Rank travel and final corridor probes as hints. Try live origin
  height, a supported local floor/threshold alternative (1.5 m clearance in
  flight), and nearby grounded-player height when materially different. Terrain
  estimates do not replace every live origin with the lowest floor. Mission Y,
  including nonzero Y, never gates final acquisition.
- Ground and flight share candidates, identity refresh, retries and interaction.
  Flight reaches the selected horizontal side, aligns within 0.9 m of its
  approach point, then closes on the live threshold. Clear direct travel allows
  early lowering; obstructed travel retains clearance and the existing planner.
  Ground commits arrival at the side before continuing inward, avoiding a loop
  back to the staging point. Ground does not demand exact marker elevation.
- Use requires <=2 m horizontally and <=3.5 m in 3D from a freshly resolved
  live door. Check the short corridor before the door face, excluding its final
  0.6 m so a closed door itself does not block use. Floor/model origin offsets
  are handled through height alternatives; being on another floor is not arrival.
- Send at most two uses per approach, four seconds apart. Log command delivery
  and await actual zoning. After another four seconds without a transition, try
  crossing 0.8 m past the threshold from that side; after eight seconds from
  the last use without zoning, advance to another side/door. Disappearance,
  changed live position, a managed use error, or 18 seconds without approach
  progress also advances acquisition. Candidates alternate before repeatedly
  retrying one building. Native Door pointers are never retained between ticks.
- If no untried live candidate is available, visit the marker and eight headings
  at 8/16/28/40 m, deriving provisional height from local floor/player data.
  A waypoint with eight seconds of no progress yields to the next. A newly
  loaded door preempts marker searching. Reaching a marker alone is not entry;
  proximity-trigger zoning still goes through the exact dungeon verification.
- Attempts and phases do not reset the acquisition progress clock. Only a new
  observed distance minimum on a finite attempt updates it. Exhausting all
  observed candidate approaches and radial waypoints plus 90 seconds without
  progress stops acquisition. The existing 15-minute overall travel limit still
  bounds the run. A single stalled door, missing door or failed use no longer
  causes a hard failure. Manual flight/ground changes regenerate approach
  geometry for the same mission without resetting the global progress clock.

Candidate association remains provisional: AOSharp's outdoor API does not
identify which nearby door belongs to a quest. The exact mission/dungeon gate
is unchanged and refuses mismatched or unidentified dungeons. Large offsets
beyond 40 m, unloaded/unexposed trigger objects, or inaccessible geometry may
still require user intervention. Dungeon exploration/combat/interior doors,
lockpicking and loot are unchanged.

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
  `Entrance candidate` / `Entrance selected` for live position, anchor offset,
  candidate properties, selected side and height source,
  `Flight path committed` for the entire waypoint sequence and replan reason,
  `complete=True/False` for complete routes versus validated sections,
  `Flight committed path leg` / `Flight committed prefix leg` for progress through that sequence, and
  `Flight blocked leg recorded` / `Flight obstacle search waiting` for observed
  obstruction and bounded search retries. `advisory=True` / `Flight committed attempt leg`
  identifies an attempted estimate when surface search is inconclusive. Watch
  `outside descent prefix` / `outside descent and entrance return`,
  `outside alignment for descent` and `descending to approach height` for roof avoidance.
  The descent search reports clear columns/drops and the direct-drop surface Y.
  Watch `Entrance scan`, `Entrance aligned`, `Entrance interaction`,
  `Entrance alternate attempt` and `Entrance search waypoint` for acquisition.
  Interaction logs distinguish sent commands from absent zoning and threshold
  crossing; alternate logs explain candidate/side changes. Flight progress
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
  and entry logs. Candidate association is provisional; unmatched or unidentified
  dungeons still stop/hold the handoff.
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
